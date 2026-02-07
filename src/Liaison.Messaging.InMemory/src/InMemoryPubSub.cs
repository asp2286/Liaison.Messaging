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
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serializer"/> is <see langword="null"/>.</exception>
    public InMemoryPubSub(IMessageSerializer serializer)
        : this(
            new MessageEnvelopeFactory(serializer ?? throw new ArgumentNullException(nameof(serializer)), new GuidMessageIdGenerator()),
            new MessageContextFactory())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryPubSub{T}"/> type.
    /// </summary>
    /// <param name="envelopeFactory">The envelope factory used during publish.</param>
    /// <param name="contextFactory">The context factory used during publish.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="envelopeFactory"/> or <paramref name="contextFactory"/> is <see langword="null"/>.
    /// </exception>
    public InMemoryPubSub(IMessageEnvelopeFactory envelopeFactory, IMessageContextFactory contextFactory)
    {
        _envelopeFactory = envelopeFactory ?? throw new ArgumentNullException(nameof(envelopeFactory));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
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
        var context = _contextFactory.Create(envelope);
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
