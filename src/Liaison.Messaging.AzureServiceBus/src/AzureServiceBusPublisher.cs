namespace Liaison.Messaging.AzureServiceBus;

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Liaison.Messaging;

/// <summary>
/// Publishes messages to an Azure Service Bus queue or topic.
/// </summary>
/// <typeparam name="T">Message payload type.</typeparam>
public sealed class AzureServiceBusPublisher<T> : IMessagePublisher<T>, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly IMessageEnvelopeFactory _envelopeFactory;
    private readonly AzureServiceBusEntityOptions _entityOptions;
    private readonly IAzureServiceBusEntityRouter? _router;
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senderCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureServiceBusPublisher{T}"/> type.
    /// </summary>
    /// <param name="client">Azure Service Bus client.</param>
    /// <param name="envelopeFactory">Envelope factory used to create outbound envelopes.</param>
    /// <param name="entityOptions">Target queue or topic configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown when required dependencies are <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the entity name is empty or whitespace.</exception>
    public AzureServiceBusPublisher(
        ServiceBusClient client,
        IMessageEnvelopeFactory envelopeFactory,
        AzureServiceBusEntityOptions entityOptions)
        : this(client, envelopeFactory, entityOptions, router: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureServiceBusPublisher{T}"/> type.
    /// </summary>
    /// <param name="client">Azure Service Bus client.</param>
    /// <param name="envelopeFactory">Envelope factory used to create outbound envelopes.</param>
    /// <param name="entityOptions">Default target queue or topic configuration.</param>
    /// <param name="router">Optional entity router. When <see langword="null"/>, the default entity options are used.</param>
    /// <exception cref="ArgumentNullException">Thrown when required dependencies are <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the entity name is empty or whitespace.</exception>
    public AzureServiceBusPublisher(
        ServiceBusClient client,
        IMessageEnvelopeFactory envelopeFactory,
        AzureServiceBusEntityOptions entityOptions,
        IAzureServiceBusEntityRouter? router)
    {
        if (string.IsNullOrWhiteSpace(entityOptions?.EntityName))
        {
            throw new ArgumentException("Entity name must be provided.", nameof(entityOptions));
        }

        _client = client ?? throw new ArgumentNullException(nameof(client));
        _envelopeFactory = envelopeFactory ?? throw new ArgumentNullException(nameof(envelopeFactory));
        _entityOptions = entityOptions;
        _router = router;
    }

    /// <inheritdoc />
    public Task PublishAsync(T message, CancellationToken cancellationToken = default)
    {
        var envelope = _envelopeFactory.Create(message, headers: null, correlationId: null);
        var resolvedOptions = _router?.ResolveForEnvelope(envelope) ?? _entityOptions;
        if (string.IsNullOrWhiteSpace(resolvedOptions.EntityName))
        {
            throw new InvalidOperationException("Resolved entity options must provide a non-empty entity name.");
        }

        var sender = _senderCache.GetOrAdd(
            resolvedOptions.EntityName,
            static (entityName, client) => client.CreateSender(entityName),
            _client);
        var serviceBusMessage = AzureServiceBusEnvelopeMapper.ToServiceBusMessage(envelope);
        return sender.SendMessageAsync(serviceBusMessage, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senderCache.Values)
        {
            await sender.DisposeAsync().ConfigureAwait(false);
        }

        _senderCache.Clear();
    }
}
