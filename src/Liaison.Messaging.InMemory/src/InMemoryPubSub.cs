namespace Liaison.Messaging.InMemory;

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liaison.Messaging;

/// <summary>
/// Provides an in-memory publish/subscribe implementation.
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
public sealed class InMemoryPubSub<T> : IMessagePublisher<T>
{
    private readonly ConcurrentDictionary<long, IMessageHandler<T>> _handlers = new();
    private readonly IMessageEnvelopeFactory _envelopeFactory;
    private readonly IMessageContextFactory _contextFactory;
    private readonly ILargePayloadPolicy? _largePayloadPolicy;
    private readonly IPayloadStore? _payloadStore;
    private long _nextHandlerId;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryPubSub{T}"/> type.
    /// </summary>
    public InMemoryPubSub()
        : this(new SystemTextJsonMessageSerializer())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryPubSub{T}"/> type using serializer-based default factories.
    /// </summary>
    /// <param name="serializer">The serializer used to encode and decode payloads.</param>
    /// <param name="largePayloadPolicy">Optional large payload policy.</param>
    /// <param name="payloadStore">Optional payload store used when a large payload policy is supplied.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serializer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when exactly one of <paramref name="largePayloadPolicy"/> or <paramref name="payloadStore"/> is provided.
    /// </exception>
    public InMemoryPubSub(
        IMessageSerializer serializer,
        ILargePayloadPolicy? largePayloadPolicy = null,
        IPayloadStore? payloadStore = null)
        : this(
            new MessageEnvelopeFactory(serializer ?? throw new ArgumentNullException(nameof(serializer)), new GuidMessageIdGenerator()),
            new MessageContextFactory(),
            largePayloadPolicy,
            payloadStore)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryPubSub{T}"/> type.
    /// </summary>
    /// <param name="envelopeFactory">The envelope factory used during publish.</param>
    /// <param name="contextFactory">The context factory used during publish.</param>
    /// <param name="largePayloadPolicy">Optional large payload policy.</param>
    /// <param name="payloadStore">Optional payload store used when a large payload policy is supplied.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="envelopeFactory"/> or <paramref name="contextFactory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when exactly one of <paramref name="largePayloadPolicy"/> or <paramref name="payloadStore"/> is provided.
    /// </exception>
    public InMemoryPubSub(
        IMessageEnvelopeFactory envelopeFactory,
        IMessageContextFactory contextFactory,
        ILargePayloadPolicy? largePayloadPolicy = null,
        IPayloadStore? payloadStore = null)
    {
        _envelopeFactory = envelopeFactory ?? throw new ArgumentNullException(nameof(envelopeFactory));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        if ((largePayloadPolicy is null) != (payloadStore is null))
        {
            throw new ArgumentException(
                "Large payload policy and payload store must both be provided or both be null.");
        }

        _largePayloadPolicy = largePayloadPolicy;
        _payloadStore = payloadStore;
    }

    /// <summary>
    /// Registers a handler subscription.
    /// </summary>
    /// <param name="handler">The handler to register.</param>
    /// <returns>An active subscription that can be disposed to unsubscribe.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="handler"/> is <see langword="null"/>.</exception>
    public IMessageSubscription Subscribe(IMessageHandler<T> handler)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var handlerId = Interlocked.Increment(ref _nextHandlerId);
        _handlers[handlerId] = handler;
        return new Subscription(_handlers, handlerId);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is <see langword="null"/>.</exception>
    public async Task PublishAsync(T message, CancellationToken cancellationToken = default)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var envelope = _envelopeFactory.Create(message);
        var deliveryEnvelope = await PrepareDeliveryEnvelopeAsync(envelope, cancellationToken).ConfigureAwait(false);
        var context = _contextFactory.Create(deliveryEnvelope);
        var snapshot = _handlers.OrderBy(pair => pair.Key).ToArray();

        foreach (var pair in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var handler = pair.Value;
            await Task.Run(
                () => handler.HandleAsync(message, context, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<MessageEnvelope> PrepareDeliveryEnvelopeAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        if (_largePayloadPolicy is null || _payloadStore is null)
        {
            return envelope;
        }

        var outboundEnvelope = await _largePayloadPolicy
            .PrepareOutboundAsync(envelope, _payloadStore, expiresAtUtc: null, cancellationToken)
            .ConfigureAwait(false);
        return await _largePayloadPolicy
            .ResolveInboundAsync(outboundEnvelope, _payloadStore, cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed class Subscription : IMessageSubscription
    {
        private readonly ConcurrentDictionary<long, IMessageHandler<T>> _handlers;
        private readonly long _handlerId;
        private int _isDisposed;

        public Subscription(ConcurrentDictionary<long, IMessageHandler<T>> handlers, long handlerId)
        {
            _handlers = handlers;
            _handlerId = handlerId;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                _handlers.TryRemove(_handlerId, out _);
            }

            return ValueTask.CompletedTask;
        }
    }
}
