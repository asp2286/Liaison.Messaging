namespace Liaison.Messaging.AzureServiceBus;

using Liaison.Messaging;

/// <summary>
/// Resolves the Azure Service Bus target entity for an outbound envelope.
/// </summary>
public interface IAzureServiceBusEntityRouter
{
    /// <summary>
    /// Resolves the target entity options for the provided envelope.
    /// </summary>
    /// <param name="envelope">Outbound envelope.</param>
    /// <returns>The resolved entity options.</returns>
    AzureServiceBusEntityOptions ResolveForEnvelope(MessageEnvelope envelope);
}
