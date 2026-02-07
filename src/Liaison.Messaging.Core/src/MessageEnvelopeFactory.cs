namespace Liaison.Messaging;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>
/// Creates immutable <see cref="MessageEnvelope"/> instances.
/// </summary>
public sealed class MessageEnvelopeFactory : IMessageEnvelopeFactory
{
    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

    private readonly IMessageSerializer _serializer;
    private readonly IMessageIdGenerator _messageIdGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageEnvelopeFactory"/> type.
    /// </summary>
    /// <param name="serializer">Serializer used for payload encoding.</param>
    /// <param name="messageIdGenerator">Message ID generator.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="serializer"/> or <paramref name="messageIdGenerator"/> is <see langword="null"/>.
    /// </exception>
    public MessageEnvelopeFactory(IMessageSerializer serializer, IMessageIdGenerator messageIdGenerator)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _messageIdGenerator = messageIdGenerator ?? throw new ArgumentNullException(nameof(messageIdGenerator));
    }

    /// <inheritdoc />
    public MessageEnvelope Create<T>(
        T message,
        IReadOnlyDictionary<string, string>? headers = null,
        string? correlationId = null,
        DateTimeOffset? sentAtUtc = null)
    {
        var body = _serializer.Serialize(message);
        var messageId = _messageIdGenerator.NewId();
        var timestamp = (sentAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var copiedHeaders = CopyHeaders(headers);

        return new MessageEnvelope(
            messageId,
            correlationId,
            timestamp,
            body,
            copiedHeaders);
    }

    private static IReadOnlyDictionary<string, string> CopyHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return EmptyHeaders;
        }

        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in headers)
        {
            copy[pair.Key] = pair.Value;
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }
}
