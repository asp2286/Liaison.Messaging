namespace Liaison.Messaging.AzureServiceBus;

using System;

/// <summary>
/// Connection settings for Azure Service Bus.
/// </summary>
public sealed record AzureServiceBusConnectionOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AzureServiceBusConnectionOptions"/> type.
    /// </summary>
    /// <param name="connectionString">Azure Service Bus connection string.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is empty or whitespace.</exception>
    public AzureServiceBusConnectionOptions(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must be provided.", nameof(connectionString));
        }

        ConnectionString = connectionString;
    }

    /// <summary>
    /// Gets the Azure Service Bus connection string.
    /// </summary>
    public string ConnectionString { get; }
}
