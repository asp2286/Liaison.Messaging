namespace Liaison.Messaging.AwsSqs;

using System;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using Liaison.Messaging;

/// <summary>
/// Publishes messages to an Amazon SQS queue.
/// </summary>
/// <typeparam name="T">Message payload type.</typeparam>
public sealed class SqsPublisher<T> : IMessagePublisher<T>, IAsyncDisposable
{
    private readonly IAmazonSQS _client;
    private readonly IMessageEnvelopeFactory _envelopeFactory;
    private readonly SqsQueueOptions _options;
    private readonly ILargePayloadPolicy? _largePayloadPolicy;
    private readonly IPayloadStore? _payloadStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqsPublisher{T}"/> type.
    /// </summary>
    /// <param name="client">Amazon SQS client.</param>
    /// <param name="envelopeFactory">Envelope factory used to create outbound envelopes.</param>
    /// <param name="options">Target queue settings.</param>
    /// <param name="largePayloadPolicy">Optional large-payload externalization policy.</param>
    /// <param name="payloadStore">Optional payload store used when a large-payload policy is configured.</param>
    /// <exception cref="ArgumentNullException">Thrown when required dependencies are <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when queue settings are invalid.</exception>
    public SqsPublisher(
        IAmazonSQS client,
        IMessageEnvelopeFactory envelopeFactory,
        SqsQueueOptions options,
        ILargePayloadPolicy? largePayloadPolicy = null,
        IPayloadStore? payloadStore = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _envelopeFactory = envelopeFactory ?? throw new ArgumentNullException(nameof(envelopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _largePayloadPolicy = largePayloadPolicy;
        _payloadStore = payloadStore;
    }

    /// <inheritdoc />
    public async Task PublishAsync(T message, CancellationToken cancellationToken = default)
    {
        var envelope = _envelopeFactory.Create(message, headers: null, correlationId: null);
        envelope = await ApplyOutboundPolicyAsync(envelope, cancellationToken).ConfigureAwait(false);

        var request = new SendMessageRequest
        {
            QueueUrl = _options.QueueUrl,
            MessageBody = SqsEnvelopeMapper.ToSqsMessageBody(envelope),
            MessageAttributes = SqsEnvelopeMapper.ToSqsMessageAttributes(envelope),
        };

        ApplyFifoOptions(request, envelope.MessageId, _options.Kind, _options.MessageGroupId, _options.UseContentBasedDeduplication);
        await _client.SendMessageAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private static void ApplyFifoOptions(
        SendMessageRequest request,
        string messageId,
        SqsQueueKind kind,
        string? messageGroupId,
        bool useContentBasedDeduplication)
    {
        if (kind != SqsQueueKind.Fifo)
        {
            return;
        }

        request.MessageGroupId = messageGroupId;
        if (!useContentBasedDeduplication)
        {
            request.MessageDeduplicationId = messageId;
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
}
