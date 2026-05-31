namespace Liaison.Messaging.AwsSqs;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using Liaison.Messaging;
using Microsoft.Extensions.Logging;

/// <summary>
/// Represents an active Amazon SQS subscription receive loop.
/// </summary>
/// <typeparam name="T">Message payload type.</typeparam>
public sealed class SqsSubscription<T> : IMessageSubscription
{
    private readonly IAmazonSQS _client;
    private readonly IMessageSerializer _serializer;
    private readonly IMessageContextFactory _contextFactory;
    private readonly SqsQueueOptions _options;
    private readonly IMessageHandler<T> _handler;
    private readonly ILogger? _logger;
    private readonly ILargePayloadPolicy? _largePayloadPolicy;
    private readonly IPayloadStore? _payloadStore;
    private readonly CancellationTokenSource _backgroundCts = new();
    private Task? _backgroundLoop;
    private int _isStarted;
    private int _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqsSubscription{T}"/> type.
    /// </summary>
    /// <param name="client">Amazon SQS client.</param>
    /// <param name="serializer">Serializer used to deserialize inbound payloads.</param>
    /// <param name="contextFactory">Factory used to create message contexts.</param>
    /// <param name="options">Queue settings.</param>
    /// <param name="handler">Message handler invoked for each received message.</param>
    /// <param name="logger">Optional logger for diagnostics. When <see langword="null"/>, receive-loop errors are silently ignored.</param>
    /// <param name="largePayloadPolicy">Optional large-payload externalization policy.</param>
    /// <param name="payloadStore">Optional payload store used when a large-payload policy is configured.</param>
    /// <exception cref="ArgumentNullException">Thrown when required dependencies are <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when queue settings are invalid.</exception>
    public SqsSubscription(
        IAmazonSQS client,
        IMessageSerializer serializer,
        IMessageContextFactory contextFactory,
        SqsQueueOptions options,
        IMessageHandler<T> handler,
        ILogger<SqsSubscription<T>>? logger = null,
        ILargePayloadPolicy? largePayloadPolicy = null,
        IPayloadStore? payloadStore = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _logger = logger;
        _largePayloadPolicy = largePayloadPolicy;
        _payloadStore = payloadStore;
    }

    /// <summary>
    /// Starts receiving messages from the configured queue.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the receive loop has been scheduled.</returns>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _isStarted, 1, 0) != 0)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _backgroundLoop = Task.Run(() => ReceiveLoopAsync(_backgroundCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _backgroundCts.Cancel();

        if (_backgroundLoop is not null)
        {
            try
            {
                await _backgroundLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _backgroundCts.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var response = await _client.ReceiveMessageAsync(
                        CreateReceiveMessageRequest(),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var messages = response.Messages ?? new List<Message>();
                foreach (var message in messages)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await ProcessMessageAsync(message, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in SQS subscription receive loop. QueueUrl={QueueUrl}", _options.QueueUrl);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private ReceiveMessageRequest CreateReceiveMessageRequest()
    {
        var request = new ReceiveMessageRequest
        {
            QueueUrl = _options.QueueUrl,
            WaitTimeSeconds = _options.WaitTimeSeconds,
            MaxNumberOfMessages = _options.MaxNumberOfMessages,
            MessageAttributeNames = new List<string> { "All" },
        };

        if (_options.VisibilityTimeoutSeconds.HasValue)
        {
            request.VisibilityTimeout = _options.VisibilityTimeoutSeconds.Value;
        }

        return request;
    }

    private async Task ProcessMessageAsync(Message message, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = SqsEnvelopeMapper.FromSqsMessage(message);
            envelope = await ApplyInboundPolicyAsync(envelope, cancellationToken).ConfigureAwait(false);
            var payload = _serializer.Deserialize<T>(envelope.Body);
            var context = _contextFactory.Create(envelope);

            await _handler.HandleAsync(payload, context, cancellationToken).ConfigureAwait(false);
            await DeleteMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to process SQS message. QueueUrl={QueueUrl} MessageId={MessageId}", _options.QueueUrl, message.MessageId);
            await TryAbandonMessageAsync(message).ConfigureAwait(false);
        }
    }

    private Task DeleteMessageAsync(Message message, CancellationToken cancellationToken)
    {
        return _client.DeleteMessageAsync(
            new DeleteMessageRequest
            {
                QueueUrl = _options.QueueUrl,
                ReceiptHandle = message.ReceiptHandle,
            },
            cancellationToken);
    }

    private async Task TryAbandonMessageAsync(Message message)
    {
        try
        {
            await _client.ChangeMessageVisibilityAsync(
                    new ChangeMessageVisibilityRequest
                    {
                        QueueUrl = _options.QueueUrl,
                        ReceiptHandle = message.ReceiptHandle,
                        VisibilityTimeout = 0,
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to abandon SQS message. QueueUrl={QueueUrl} MessageId={MessageId}", _options.QueueUrl, message.MessageId);
        }
    }

    private Task<MessageEnvelope> ApplyInboundPolicyAsync(
        MessageEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (_largePayloadPolicy is null)
        {
            return Task.FromResult(envelope);
        }

        if (_payloadStore is null)
        {
            throw new InvalidOperationException(
                "ILargePayloadPolicy is configured but no IPayloadStore is registered.");
        }

        return _largePayloadPolicy.ResolveInboundAsync(envelope, _payloadStore, ct: cancellationToken);
    }
}
