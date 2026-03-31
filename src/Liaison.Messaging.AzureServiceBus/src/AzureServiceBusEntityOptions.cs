namespace Liaison.Messaging.AzureServiceBus;

using System;

/// <summary>
/// Defines the target Azure Service Bus entity configuration.
/// </summary>
public sealed record AzureServiceBusEntityOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AzureServiceBusEntityOptions"/> type
    /// with default values for use with the <c>Action&lt;AzureServiceBusEntityOptions&gt;</c> configuration pattern.
    /// </summary>
    public AzureServiceBusEntityOptions()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureServiceBusEntityOptions"/> type.
    /// </summary>
    /// <param name="entityName">Queue or topic name.</param>
    /// <param name="kind">Entity kind.</param>
    /// <param name="subscriptionName">Optional subscription name for topic receive operations.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="entityName"/> is empty or whitespace.</exception>
    public AzureServiceBusEntityOptions(
        string entityName,
        AzureServiceBusEntityKind kind,
        string? subscriptionName = null)
    {
        if (string.IsNullOrWhiteSpace(entityName))
        {
            throw new ArgumentException("Entity name must be provided.", nameof(entityName));
        }

        EntityName = entityName;
        Kind = kind;
        SubscriptionName = subscriptionName;
    }

    /// <summary>
    /// Gets or sets the queue or topic name.
    /// </summary>
    public string EntityName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the entity kind.
    /// </summary>
    public AzureServiceBusEntityKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the subscription name when a topic subscription is used for receive operations.
    /// </summary>
    public string? SubscriptionName { get; set; }
}
