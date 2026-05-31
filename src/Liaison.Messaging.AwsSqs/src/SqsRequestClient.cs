namespace Liaison.Messaging.AwsSqs;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using Liaison.Messaging;
using Microsoft.Extensions.Logging;

/// <summary>
/// Sends request messages over Amazon SQS and awaits correlated replies.
/// </summary>
/// <typeparam name="TRequest">Request payload type.</typeparam>
/// <typeparam name="TReply">Reply payload type.</typeparam>
public sealed class SqsRequestClient<TRequest, TReply> : IRequestClient<TRequest, TReply>, IAsyncDisposable
{
    private readonly IAmazonSQS _client;
    private readonly IMessageEnvelopeFactory _envelopeFactory;
    private readonly IMessageSerializer _serializer;
    private readonly IRequestTimeoutPolicy? _timeoutPolicy;
    private readonly SqsRequestReplyOptions _options;
    private readonly ILogger? _logger;
    private readonly ILargePayloadPolicy? _largePayloadPolicy;
    private readonly IPayloadStore? _payloadStore;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<Message>> _pendingRequests = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _backgroundCts = new();
    private readonly Task _backgroundLoop;
    private int _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqsRequestClient{TRequest, TReply}"/> type.
    /// </summary>
    /// <param name="client">Amazon SQS client.</param>
    /// <param name="envelopeFactory">Envelope factory used for request envelopes.</param>
    /// <param name="serializer">Serializer used for reply deserialization.</param>
    /// <param name="timeoutPolicy">Optional timeout policy.</param>
    /// <param name="options">Request/reply queue settings.</param>
    /// <param name="logger">Optional logger for diagnostics. When <see langword="null"/>, receive-loop errors are silently ignored.</param>
    /// <param name="largePayloadPolicy">Optional large-payload externalization policy.</param>
    /// <param name="payloadStore">Optional payload store used when a large-payload policy is configured.</param>
    /// <exception cref="ArgumentNullException">Thrown when required dependencies are <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when request/reply settings are invalid.</exception>
    public SqsRequestClient(
        IAmazonSQS client,
        IMessageEnvelopeFactory envelopeFactory,
        IMessageSerializer serializer,
        IRequestTimeoutPolicy? timeoutPolicy,
        SqsRequestReplyOptions options,
        ILogger<SqsRequestClient<TRequest, TReply>>? logger = null,
        ILargePayloadPolicy? largePayloadPolicy = null,
        IPayloadStore? payloadStore = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _envelopeFactory = envelopeFactory ?? throw new ArgumentNullException(nameof(envelopeFactory));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _timeoutPolicy = timeoutPolicy;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _logger = logger;
        _largePayloadPolicy = largePayloadPolicy;
        _payloadStore = payloadStore;
        _backgroundLoop = Task.Run(() => ReceiveLoopAsync(_backgroundCts.Token));
    }

    /// <inheritdoc />
    public async Task<Reply<TReply>> SendAsync(TRequest request, CancellationToken cancellationToken = default)
    {
        var timeout = _timeoutPolicy?.GetTimeout(headers: null) ?? _options.DefaultTimeout;
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            return new Reply<TReply>(ReplyStatus.Failure, value: default, error: "Timeout policy returned an invalid timeout.");
        }

        using var timeoutCts = timeout == Timeout.InfiniteTimeSpan ? null : new CancellationTokenSource(timeout);
        using var linkedCts = timeoutCts is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var token = linkedCts.Token;

        var requestEnvelope = _envelopeFactory.Create(request, headers: null, correlationId: null);
        var correlationId = requestEnvelope.MessageId;

        var tcs = new TaskCompletionSource<Message>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingRequests.TryAdd(correlationId, tcs))
        {
            var duplicateCorrelationIdException =
                new InvalidOperationException("Duplicate correlation ID detected. This is a bug.");
            tcs.TrySetException(duplicateCorrelationIdException);
            return new Reply<TReply>(
                ReplyStatus.Failure,
                value: default,
                error: duplicateCorrelationIdException.Message);
        }

        try
        {
            requestEnvelope = await ApplyOutboundPolicyAsync(requestEnvelope, token).ConfigureAwait(false);
            requestEnvelope = WithCorrelationId(requestEnvelope, correlationId);

            var requestMessage = new SendMessageRequest
            {
                QueueUrl = _options.RequestQueueUrl,
                MessageBody = SqsEnvelopeMapper.ToSqsMessageBody(requestEnvelope),
                MessageAttributes = SqsEnvelopeMapper.ToSqsMessageAttributes(requestEnvelope),
            };
            ApplyFifoOptions(requestMessage, requestEnvelope.MessageId);

            await _client.SendMessageAsync(requestMessage, token).ConfigureAwait(false);

            using var registration = token.Register(
                static state => ((TaskCompletionSource<Message>)state!).TrySetCanceled(),
                tcs);

            var receivedMessage = await tcs.Task.ConfigureAwait(false);
            var replyEnvelope = SqsEnvelopeMapper.FromSqsMessage(receivedMessage);
            replyEnvelope = await ApplyInboundPolicyAsync(replyEnvelope, CancellationToken.None).ConfigureAwait(false);

            if (!TryGetStatusHeaders(replyEnvelope.Headers, out var status, out var error))
            {
                var value = _serializer.Deserialize<TReply>(replyEnvelope.Body);
                return new Reply<TReply>(ReplyStatus.Success, value, error: null);
            }

            if (status != ReplyStatus.Success)
            {
                return new Reply<TReply>(status, value: default, error: error);
            }

            var successValue = _serializer.Deserialize<TReply>(replyEnvelope.Body);
            return new Reply<TReply>(ReplyStatus.Success, successValue, error: null);
        }
        catch (OperationCanceledException ex) when (timeoutCts is not null && timeoutCts.IsCancellationRequested)
        {
            return new Reply<TReply>(ReplyStatus.Timeout, value: default, error: ex.Message);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            return new Reply<TReply>(ReplyStatus.Timeout, value: default, error: ex.Message);
        }
        catch (Exception ex)
        {
            return new Reply<TReply>(ReplyStatus.Failure, value: default, error: ex.Message);
        }
        finally
        {
            _pendingRequests.TryRemove(correlationId, out _);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _backgroundCts.Cancel();

        try
        {
            await _backgroundLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        foreach (var pair in _pendingRequests)
        {
            pair.Value.TrySetCanceled();
        }

        _pendingRequests.Clear();
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

                    try
                    {
                        await DispatchReplyAsync(message, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Failed to dispatch SQS reply message. ReplyQueueUrl={ReplyQueueUrl}", _options.ReplyQueueUrl);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in SQS reply receive loop. ReplyQueueUrl={ReplyQueueUrl}", _options.ReplyQueueUrl);

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
            QueueUrl = _options.ReplyQueueUrl,
            WaitTimeSeconds = _options.WaitTimeSeconds,
            MaxNumberOfMessages = _options.MaxNumberOfMessages,
            MessageAttributeNames = new List<string> { "All" },
        };
    }

    private async Task DispatchReplyAsync(Message message, CancellationToken cancellationToken)
    {
        var correlationId = SqsEnvelopeMapper.TryReadCorrelationId(message);
        if (!string.IsNullOrWhiteSpace(correlationId) &&
            _pendingRequests.TryRemove(correlationId, out var tcs))
        {
            try
            {
                _ = SqsEnvelopeMapper.FromSqsMessage(message);
                await DeleteReplyMessageAsync(message, cancellationToken).ConfigureAwait(false);
                tcs.TrySetResult(message);
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    ex,
                    "Failed to delete matched SQS reply message. CorrelationId={CorrelationId}",
                    correlationId);

                try
                {
                    await DeleteReplyMessageAsync(message, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception deleteEx)
                {
                    _logger?.LogError(
                        deleteEx,
                        "Failed to delete malformed matched SQS reply message. CorrelationId={CorrelationId}",
                        correlationId);
                }

                tcs.TrySetException(ex);
            }
        }
        else
        {
            try
            {
                await DeleteReplyMessageAsync(message, cancellationToken).ConfigureAwait(false);
                _logger?.LogWarning(
                    "Deleted unmatched SQS reply message. CorrelationId={CorrelationId}",
                    correlationId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogError(
                    ex,
                    "Failed to delete unmatched SQS reply message. CorrelationId={CorrelationId}",
                    correlationId);
            }
        }
    }

    private Task DeleteReplyMessageAsync(Message message, CancellationToken cancellationToken)
    {
        return _client.DeleteMessageAsync(
            new DeleteMessageRequest
            {
                QueueUrl = _options.ReplyQueueUrl,
                ReceiptHandle = message.ReceiptHandle,
            },
            cancellationToken);
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

    private static MessageEnvelope WithCorrelationId(MessageEnvelope envelope, string correlationId)
    {
        return new MessageEnvelope(
            envelope.MessageId,
            correlationId,
            envelope.SentAtUtc,
            envelope.Body,
            envelope.Headers);
    }

    private Task<MessageEnvelope> ApplyOutboundPolicyAsync(
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

        return _largePayloadPolicy.PrepareOutboundAsync(
            envelope, _payloadStore, expiresAtUtc: null, ct: cancellationToken);
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

    private static bool TryGetStatusHeaders(
        IReadOnlyDictionary<string, string> headers,
        out ReplyStatus status,
        out string? error)
    {
        if (!headers.TryGetValue(SqsReplyHeaders.Status, out var statusText))
        {
            status = ReplyStatus.Success;
            error = null;
            return false;
        }

        if (!Enum.TryParse(statusText, ignoreCase: true, out status))
        {
            status = ReplyStatus.Failure;
        }

        headers.TryGetValue(SqsReplyHeaders.Error, out error);
        return true;
    }
}
