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
/// Processes request messages from Amazon SQS and sends correlated replies.
/// </summary>
/// <typeparam name="TRequest">Request payload type.</typeparam>
/// <typeparam name="TReply">Reply payload type.</typeparam>
public sealed class SqsRequestProcessor<TRequest, TReply> : IAsyncDisposable
{
    private readonly IAmazonSQS _client;
    private readonly IMessageSerializer _serializer;
    private readonly IMessageContextFactory _contextFactory;
    private readonly SqsRequestReplyOptions _options;
    private readonly IRequestHandler<TRequest, TReply> _handler;
    private readonly ILogger? _logger;
    private readonly ILargePayloadPolicy? _largePayloadPolicy;
    private readonly IPayloadStore? _payloadStore;
    private readonly IMessageIdGenerator _messageIdGenerator;
    private readonly CancellationTokenSource _backgroundCts = new();
    private Task? _backgroundLoop;
    private int _isStarted;
    private int _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqsRequestProcessor{TRequest, TReply}"/> type.
    /// </summary>
    /// <param name="client">Amazon SQS client.</param>
    /// <param name="serializer">Serializer used for request and reply payloads.</param>
    /// <param name="contextFactory">Factory used to create request contexts.</param>
    /// <param name="options">Request/reply queue settings.</param>
    /// <param name="handler">Request handler.</param>
    /// <param name="logger">Optional logger for diagnostics. When <see langword="null"/>, receive-loop errors are silently ignored.</param>
    /// <param name="largePayloadPolicy">Optional large-payload externalization policy.</param>
    /// <param name="payloadStore">Optional payload store used when a large-payload policy is configured.</param>
    /// <exception cref="ArgumentNullException">Thrown when required dependencies are <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when request/reply settings are invalid.</exception>
    public SqsRequestProcessor(
        IAmazonSQS client,
        IMessageSerializer serializer,
        IMessageContextFactory contextFactory,
        SqsRequestReplyOptions options,
        IRequestHandler<TRequest, TReply> handler,
        ILogger<SqsRequestProcessor<TRequest, TReply>>? logger = null,
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
        _messageIdGenerator = new GuidMessageIdGenerator();
    }

    /// <summary>
    /// Starts processing request messages from the configured queue.
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
                _logger?.LogError(ex, "Error in SQS request receive loop. RequestQueueUrl={RequestQueueUrl}", _options.RequestQueueUrl);

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
        return new ReceiveMessageRequest
        {
            QueueUrl = _options.RequestQueueUrl,
            WaitTimeSeconds = _options.WaitTimeSeconds,
            MaxNumberOfMessages = _options.MaxNumberOfMessages,
            MessageAttributeNames = new List<string> { "All" },
        };
    }

    private async Task ProcessMessageAsync(Message message, CancellationToken cancellationToken)
    {
        MessageEnvelope requestEnvelope;
        string? correlationId;

        try
        {
            requestEnvelope = SqsEnvelopeMapper.FromSqsMessage(message);
            correlationId = string.IsNullOrWhiteSpace(requestEnvelope.MessageId)
                ? requestEnvelope.CorrelationId
                : requestEnvelope.MessageId;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to map SQS request message. RequestQueueUrl={RequestQueueUrl} MessageId={MessageId}", _options.RequestQueueUrl, message.MessageId);
            await TryAbandonMessageAsync(message, correlationId: null, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        TReply? replyPayload = default;
        ReplyStatus? replyStatus = null;
        string? replyError = null;

        try
        {
            requestEnvelope = await ApplyInboundPolicyAsync(requestEnvelope, cancellationToken)
                .ConfigureAwait(false);
            var request = _serializer.Deserialize<TRequest>(requestEnvelope.Body);
            var context = _contextFactory.Create(requestEnvelope);
            replyPayload = await _handler.HandleAsync(request, context, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            replyStatus = ReplyStatus.ValidationError;
            replyError = ex.Message;
        }
        catch (Exception)
        {
            replyStatus = ReplyStatus.Failure;
            replyError = "Request processing failed.";
        }

        try
        {
            await SendReplyAsync(replyPayload, correlationId, replyStatus, replyError, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send SQS reply for request. CorrelationId={CorrelationId}", correlationId);
            await TryAbandonMessageAsync(message, correlationId, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        try
        {
            await DeleteRequestMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to complete SQS request message. CorrelationId={CorrelationId}", correlationId);
            await TryAbandonMessageAsync(message, correlationId, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task SendReplyAsync(
        TReply? value,
        string? correlationId,
        ReplyStatus? status,
        string? error,
        CancellationToken cancellationToken)
    {
        var payload = value is null ? Array.Empty<byte>() : _serializer.Serialize(value);
        var messageId = _messageIdGenerator.NewId();
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);

        if (status.HasValue)
        {
            headers[SqsReplyHeaders.Status] = status.Value.ToString();
            if (!string.IsNullOrWhiteSpace(error))
            {
                headers[SqsReplyHeaders.Error] = error;
            }
        }

        var replyEnvelope = new MessageEnvelope(
            messageId,
            correlationId,
            DateTimeOffset.UtcNow,
            payload,
            headers);

        if (_largePayloadPolicy is not null)
        {
            if (_payloadStore is null)
            {
                throw new InvalidOperationException(
                    "ILargePayloadPolicy is configured but no IPayloadStore is registered.");
            }

            replyEnvelope = await _largePayloadPolicy.PrepareOutboundAsync(
                    replyEnvelope, _payloadStore, expiresAtUtc: null, ct: cancellationToken)
                .ConfigureAwait(false);
        }

        var request = new SendMessageRequest
        {
            QueueUrl = _options.ReplyQueueUrl,
            MessageBody = SqsEnvelopeMapper.ToSqsMessageBody(replyEnvelope),
            MessageAttributes = SqsEnvelopeMapper.ToSqsMessageAttributes(replyEnvelope),
        };
        ApplyFifoOptions(request, replyEnvelope.MessageId);

        await _client.SendMessageAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private Task DeleteRequestMessageAsync(Message message, CancellationToken cancellationToken)
    {
        return _client.DeleteMessageAsync(
            new DeleteMessageRequest
            {
                QueueUrl = _options.RequestQueueUrl,
                ReceiptHandle = message.ReceiptHandle,
            },
            cancellationToken);
    }

    private async Task TryAbandonMessageAsync(
        Message message,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _client.ChangeMessageVisibilityAsync(
                    new ChangeMessageVisibilityRequest
                    {
                        QueueUrl = _options.RequestQueueUrl,
                        ReceiptHandle = message.ReceiptHandle,
                        VisibilityTimeout = 0,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to abandon SQS request message. CorrelationId={CorrelationId}", correlationId);
        }
    }

    private void ApplyFifoOptions(SendMessageRequest request, string messageId)
    {
        if (_options.Kind != SqsQueueKind.Fifo)
        {
            return;
        }

        request.MessageGroupId = _options.MessageGroupId;
        if (!_options.UseContentBasedDeduplication)
        {
            request.MessageDeduplicationId = messageId;
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
