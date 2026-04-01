namespace Liaison.Messaging.AzureServiceBus;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Liaison.Messaging;
using Microsoft.Extensions.Logging;

/// <summary>
/// Sends request messages over Azure Service Bus and awaits correlated replies.
/// </summary>
/// <typeparam name="TRequest">Request payload type.</typeparam>
/// <typeparam name="TReply">Reply payload type.</typeparam>
public sealed class AzureServiceBusRequestClient<TRequest, TReply> : IRequestClient<TRequest, TReply>, IAsyncDisposable
{
    private readonly IMessageEnvelopeFactory _envelopeFactory;
    private readonly IMessageSerializer _serializer;
    private readonly IRequestTimeoutPolicy? _timeoutPolicy;
    private readonly ILogger? _logger;
    private readonly ILargePayloadPolicy? _largePayloadPolicy;
    private readonly IPayloadStore? _payloadStore;
    private readonly AzureServiceBusRequestReplyOptions _options;
    private readonly ServiceBusSender _requestSender;
    private readonly ServiceBusReceiver _replyReceiver;

    // Correlation-based reply dispatch: a shared background loop receives all reply
    // messages and dispatches them to the matching TaskCompletionSource by correlation ID.
    // This is safe for concurrent SendAsync calls because each call registers its own TCS
    // before sending the request. Unmatched replies are dead-lettered.
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ServiceBusReceivedMessage>> _pendingRequests = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _backgroundCts = new();
    private readonly Task _backgroundLoop;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureServiceBusRequestClient{TRequest, TReply}"/> type.
    /// </summary>
    /// <param name="client">Azure Service Bus client.</param>
    /// <param name="envelopeFactory">Envelope factory used for request envelopes.</param>
    /// <param name="serializer">Serializer used for reply deserialization.</param>
    /// <param name="timeoutPolicy">Optional timeout policy.</param>
    /// <param name="options">Request/reply queue settings.</param>
    /// <param name="logger">Optional logger for diagnostics. When <see langword="null"/>, receive-loop errors are silently ignored.</param>
    /// <param name="largePayloadPolicy">Optional large-payload externalization policy.</param>
    /// <param name="payloadStore">Optional payload store used when a large-payload policy is configured.</param>
    /// <exception cref="ArgumentNullException">Thrown when required dependencies are <see langword="null"/>.</exception>
    public AzureServiceBusRequestClient(
        ServiceBusClient client,
        IMessageEnvelopeFactory envelopeFactory,
        IMessageSerializer serializer,
        IRequestTimeoutPolicy? timeoutPolicy,
        AzureServiceBusRequestReplyOptions options,
        ILogger<AzureServiceBusRequestClient<TRequest, TReply>>? logger = null,
        ILargePayloadPolicy? largePayloadPolicy = null,
        IPayloadStore? payloadStore = null)
    {
        _envelopeFactory = envelopeFactory ?? throw new ArgumentNullException(nameof(envelopeFactory));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _timeoutPolicy = timeoutPolicy;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _largePayloadPolicy = largePayloadPolicy;
        _payloadStore = payloadStore;

        var busClient = client ?? throw new ArgumentNullException(nameof(client));
        _requestSender = busClient.CreateSender(_options.RequestQueueName);
        _replyReceiver = busClient.CreateReceiver(_options.ReplyQueueName, new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
        });

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

        var tcs = new TaskCompletionSource<ServiceBusReceivedMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
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

            var requestMessage = AzureServiceBusEnvelopeMapper.ToServiceBusMessage(requestEnvelope);
            requestMessage.CorrelationId = correlationId;
            requestMessage.ReplyTo = _options.ReplyQueueName;

            await _requestSender.SendMessageAsync(requestMessage, token).ConfigureAwait(false);

            // Register cancellation to unblock the TCS when the token fires.
            using var registration = token.Register(static state => ((TaskCompletionSource<ServiceBusReceivedMessage>)state!).TrySetCanceled(), tcs);

            var receivedMessage = await tcs.Task.ConfigureAwait(false);

            // Complete the reply message since we have successfully received it.
            await _replyReceiver.CompleteMessageAsync(receivedMessage, CancellationToken.None).ConfigureAwait(false);

            var replyEnvelope = AzureServiceBusEnvelopeMapper.FromServiceBusReceivedMessage(receivedMessage);
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
        _backgroundCts.Cancel();

        try
        {
            await _backgroundLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }

        // Cancel all pending requests so callers are unblocked.
        foreach (var pair in _pendingRequests)
        {
            pair.Value.TrySetCanceled();
        }

        _pendingRequests.Clear();

        await _requestSender.DisposeAsync().ConfigureAwait(false);
        await _replyReceiver.DisposeAsync().ConfigureAwait(false);
        _backgroundCts.Dispose();
    }

    /// <summary>
    /// Background loop that receives reply messages and dispatches them to pending callers.
    /// </summary>
    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var messages = await _replyReceiver.ReceiveMessagesAsync(
                        maxMessages: 10,
                        maxWaitTime: TimeSpan.FromSeconds(1),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                foreach (var message in messages)
                {
                    if (!string.IsNullOrEmpty(message.CorrelationId) &&
                        _pendingRequests.TryRemove(message.CorrelationId, out var tcs))
                    {
                        tcs.TrySetResult(message);
                    }
                    else
                    {
                        // Unmatched replies are dead-lettered. Reply queues must not be shared
                        // across independent client instances. Each client instance should have
                        // a dedicated reply queue.
                        try
                        {
                            await _replyReceiver.DeadLetterMessageAsync(
                                    message,
                                    deadLetterReason: "NoMatchingRequest",
                                    deadLetterErrorDescription: "No pending request matched the correlation ID.",
                                    cancellationToken: cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger?.LogError(
                                ex,
                                "Failed to dead-letter unmatched reply message. CorrelationId={CorrelationId}",
                                message.CorrelationId);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in reply receive loop.");
                // Known limitation by design: pending request TCS entries are not faulted here.
                // They complete through normal timeout/cancellation/disposal paths.

                // Brief delay to avoid tight spin on persistent errors.
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
        if (!headers.TryGetValue(AzureServiceBusReplyHeaders.Status, out var statusText))
        {
            status = ReplyStatus.Success;
            error = null;
            return false;
        }

        if (!Enum.TryParse(statusText, ignoreCase: true, out status))
        {
            status = ReplyStatus.Failure;
        }

        headers.TryGetValue(AzureServiceBusReplyHeaders.Error, out error);
        return true;
    }
}
