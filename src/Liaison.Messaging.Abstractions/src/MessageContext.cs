namespace Liaison.Messaging;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>
/// Represents handler-facing metadata for a received message.
/// </summary>
public sealed record MessageContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MessageContext"/> type.
    /// </summary>
    /// <param name="messageId">The unique message identifier.</param>
    /// <param name="correlationId">An optional correlation identifier.</param>
    /// <param name="headers">Transport-neutral message headers.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="messageId"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="headers"/> is <see langword="null"/>.</exception>
    public MessageContext(
        string messageId,
        string? correlationId,
        IReadOnlyDictionary<string, string> headers)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            throw new ArgumentException("Message ID must be provided.", nameof(messageId));
        }

        MessageId = messageId;
        CorrelationId = correlationId;
        Headers = ToReadOnlyHeaders(headers);
    }

    /// <summary>
    /// Gets the unique message identifier.
    /// </summary>
    public string MessageId { get; }

    /// <summary>
    /// Gets the optional correlation identifier.
    /// </summary>
    public string? CorrelationId { get; }

    /// <summary>
    /// Gets transport-neutral message headers.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    private static IReadOnlyDictionary<string, string> ToReadOnlyHeaders(IReadOnlyDictionary<string, string> headers)
    {
        if (headers is null)
        {
            throw new ArgumentNullException(nameof(headers));
        }

        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in headers)
        {
            copy[pair.Key] = pair.Value;
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }
}
