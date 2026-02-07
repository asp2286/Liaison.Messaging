namespace Liaison.Messaging;

using System;
using System.Collections.Generic;

/// <summary>
/// Creates transport-facing message envelopes.
/// </summary>
public interface IMessageEnvelopeFactory
{
    /// <summary>
    /// Creates a message envelope from a payload and metadata.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="message">The message payload.</param>
    /// <param name="headers">Optional transport-neutral headers.</param>
    /// <param name="correlationId">Optional correlation identifier.</param>
    /// <param name="sentAtUtc">Optional sent timestamp. The resulting envelope always stores UTC.</param>
    /// <returns>A new immutable message envelope.</returns>
    MessageEnvelope Create<T>(
        T message,
        IReadOnlyDictionary<string, string>? headers = null,
        string? correlationId = null,
        DateTimeOffset? sentAtUtc = null);
}
