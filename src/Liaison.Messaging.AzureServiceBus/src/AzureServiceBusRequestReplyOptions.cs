namespace Liaison.Messaging.AzureServiceBus;

using System;
using System.Threading;

/// <summary>
/// Defines Azure Service Bus request/reply queue settings.
/// </summary>
public sealed record AzureServiceBusRequestReplyOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AzureServiceBusRequestReplyOptions"/> type.
    /// </summary>
    /// <param name="requestQueueName">Request queue name.</param>
    /// <param name="replyQueueName">Reply queue name.</param>
    /// <param name="defaultTimeout">Default request timeout when no timeout policy is provided.</param>
    /// <exception cref="ArgumentException">Thrown when queue names are empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="defaultTimeout"/> is invalid.</exception>
    public AzureServiceBusRequestReplyOptions(
        string requestQueueName,
        string replyQueueName,
        TimeSpan defaultTimeout)
    {
        if (string.IsNullOrWhiteSpace(requestQueueName))
        {
            throw new ArgumentException("Request queue name must be provided.", nameof(requestQueueName));
        }

        if (string.IsNullOrWhiteSpace(replyQueueName))
        {
            throw new ArgumentException("Reply queue name must be provided.", nameof(replyQueueName));
        }

        if (defaultTimeout < TimeSpan.Zero && defaultTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultTimeout), "Timeout must be non-negative or infinite.");
        }

        RequestQueueName = requestQueueName;
        ReplyQueueName = replyQueueName;
        DefaultTimeout = defaultTimeout;
    }

    /// <summary>
    /// Gets the request queue name.
    /// </summary>
    public string RequestQueueName { get; }

    /// <summary>
    /// Gets the reply queue name.
    /// </summary>
    public string ReplyQueueName { get; }

    /// <summary>
    /// Gets the default timeout value.
    /// </summary>
    public TimeSpan DefaultTimeout { get; }
}
