namespace Liaison.Messaging.AzureServiceBus;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Liaison.Messaging;

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
    private readonly AzureServiceBusRequestReplyOptions _options;
    private readonly ServiceBusSender _requestSender;
    private readonly ServiceBusReceiver _replyReceiver;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureServiceBusRequestClient{TRequest, TReply}"/> type.
    /// </summary>
    /// <param name="client">Azure Service Bus client.</param>
    /// <param name="envelopeFactory">Envelope factory used for request envelopes.</param>
    /// <param name="serializer">Serializer used for reply deserialization.</param>
    /// <param name="timeoutPolicy">Optional timeout policy.</param>
    /// <param name="options">Request/reply queue settings.</param>
    /// <exception cref="ArgumentNullException">Thrown when required dependencies are <see langword="null"/>.</exception>
    public AzureServiceBusRequestClient(
        ServiceBusClient client,
        IMessageEnvelopeFactory envelopeFactory,
        IMessageSerializer serializer,
        IRequestTimeoutPolicy? timeoutPolicy,
        AzureServiceBusRequestReplyOptions options)
    {
        _envelopeFactory = envelopeFactory ?? throw new ArgumentNullException(nameof(envelopeFactory));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _timeoutPolicy = timeoutPolicy;
        _options = options ?? throw new ArgumentNullException(nameof(options));

        var busClient = client ?? throw new ArgumentNullException(nameof(client));
        _requestSender = busClient.CreateSender(_options.RequestQueueName);
        _replyReceiver = busClient.CreateReceiver(_options.ReplyQueueName, new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
        });
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

        try
        {
            var requestEnvelope = _envelopeFactory.Create(request, headers: null, correlationId: null);
            var requestMessage = AzureServiceBusEnvelopeMapper.ToServiceBusMessage(requestEnvelope);
            requestMessage.CorrelationId = requestEnvelope.MessageId;
            requestMessage.ReplyTo = _options.ReplyQueueName;

            await _requestSender.SendMessageAsync(requestMessage, token).ConfigureAwait(false);

            while (true)
            {
                var receivedMessages = await _replyReceiver.ReceiveMessagesAsync(
                        maxMessages: 10,
                        maxWaitTime: TimeSpan.FromSeconds(1),
                        cancellationToken: token)
                    .ConfigureAwait(false);

                foreach (var receivedMessage in receivedMessages)
                {
                    if (!string.Equals(receivedMessage.CorrelationId, requestEnvelope.MessageId, StringComparison.Ordinal))
                    {
                        await _replyReceiver.AbandonMessageAsync(receivedMessage, cancellationToken: token).ConfigureAwait(false);
                        continue;
                    }

                    var replyEnvelope = AzureServiceBusEnvelopeMapper.FromServiceBusReceivedMessage(receivedMessage);
                    if (!TryGetStatusHeaders(replyEnvelope.Headers, out var status, out var error))
                    {
                        var value = _serializer.Deserialize<TReply>(replyEnvelope.Body);
                        await _replyReceiver.CompleteMessageAsync(receivedMessage, token).ConfigureAwait(false);
                        return new Reply<TReply>(ReplyStatus.Success, value, error: null);
                    }

                    if (status != ReplyStatus.Success)
                    {
                        await _replyReceiver.CompleteMessageAsync(receivedMessage, token).ConfigureAwait(false);
                        return new Reply<TReply>(status, value: default, error: error);
                    }

                    var successValue = _serializer.Deserialize<TReply>(replyEnvelope.Body);
                    await _replyReceiver.CompleteMessageAsync(receivedMessage, token).ConfigureAwait(false);
                    return new Reply<TReply>(ReplyStatus.Success, successValue, error: null);
                }
            }
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
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _requestSender.DisposeAsync().ConfigureAwait(false);
        await _replyReceiver.DisposeAsync().ConfigureAwait(false);
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
