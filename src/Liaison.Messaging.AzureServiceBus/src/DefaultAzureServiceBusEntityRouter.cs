namespace Liaison.Messaging.AzureServiceBus;

using System;
using Liaison.Messaging;

/// <summary>
/// Resolves Azure Service Bus entities from optional semantic headers.
/// </summary>
public sealed class DefaultAzureServiceBusEntityRouter : IAzureServiceBusEntityRouter
{
    private readonly AzureServiceBusEntityOptions _queueOptions;
    private readonly AzureServiceBusEntityOptions _topicOptions;
    private readonly AzureServiceBusEntityOptions? _requestQueueOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAzureServiceBusEntityRouter"/> type.
    /// </summary>
    /// <param name="queueOptions">Default queue options.</param>
    /// <param name="topicOptions">Topic options for event fan-out.</param>
    /// <param name="requestQueueOptions">Optional explicit request queue options.</param>
    /// <exception cref="ArgumentNullException">Thrown when required options are <see langword="null"/>.</exception>
    public DefaultAzureServiceBusEntityRouter(
        AzureServiceBusEntityOptions queueOptions,
        AzureServiceBusEntityOptions topicOptions,
        AzureServiceBusEntityOptions? requestQueueOptions = null)
    {
        _queueOptions = queueOptions ?? throw new ArgumentNullException(nameof(queueOptions));
        _topicOptions = topicOptions ?? throw new ArgumentNullException(nameof(topicOptions));
        _requestQueueOptions = requestQueueOptions;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="envelope"/> is <see langword="null"/>.</exception>
    public AzureServiceBusEntityOptions ResolveForEnvelope(MessageEnvelope envelope)
    {
        if (envelope is null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        if (!envelope.Headers.TryGetValue(AzureServiceBusSemanticHeaders.Kind, out var kind))
        {
            return _queueOptions;
        }

        if (string.Equals(kind, AzureServiceBusSemanticHeaders.KindEvent, StringComparison.OrdinalIgnoreCase))
        {
            return _topicOptions;
        }

        if (string.Equals(kind, AzureServiceBusSemanticHeaders.KindCommand, StringComparison.OrdinalIgnoreCase))
        {
            return _queueOptions;
        }

        if (string.Equals(kind, AzureServiceBusSemanticHeaders.KindRequest, StringComparison.OrdinalIgnoreCase))
        {
            return _requestQueueOptions ?? _queueOptions;
        }

        return _queueOptions;
    }
}
