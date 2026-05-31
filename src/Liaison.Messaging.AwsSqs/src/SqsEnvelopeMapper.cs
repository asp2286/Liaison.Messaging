namespace Liaison.Messaging.AwsSqs;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Amazon.SQS.Model;
using Liaison.Messaging;

internal static class SqsEnvelopeMapper
{
    // SQS limits each message to 10 attributes and 256 KiB total size. Base64
    // inflates payload bytes by roughly 33%, so configure large-payload
    // externalization below the raw payload ceiling when messages approach 192 KiB.
    public static string ToSqsMessageBody(MessageEnvelope envelope)
    {
        if (envelope is null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        // SQS message bodies are strings. Base64 preserves the serializer's binary
        // payload bytes exactly while keeping the mapper free of serializer concerns.
        return Convert.ToBase64String(envelope.Body.ToArray());
    }

    public static Dictionary<string, MessageAttributeValue> ToSqsMessageAttributes(MessageEnvelope envelope)
    {
        if (envelope is null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        var attributes = new Dictionary<string, MessageAttributeValue>(StringComparer.Ordinal);
        foreach (var header in envelope.Headers)
        {
            attributes[header.Key] = CreateStringAttribute(header.Value);
        }

        attributes[SqsSemanticHeaders.MessageId] = CreateStringAttribute(envelope.MessageId);

        if (!string.IsNullOrWhiteSpace(envelope.CorrelationId))
        {
            attributes[SqsSemanticHeaders.CorrelationId] = CreateStringAttribute(envelope.CorrelationId);
        }

        return attributes;
    }

    public static MessageEnvelope FromSqsMessage(
        Message message,
        IReadOnlyDictionary<string, string>? extra = null)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        if (message.MessageAttributes is not null)
        {
            foreach (var attribute in message.MessageAttributes)
            {
                if (!IsStringAttribute(attribute.Value))
                {
                    continue;
                }

                if (string.Equals(attribute.Key, SqsSemanticHeaders.MessageId, StringComparison.Ordinal) ||
                    string.Equals(attribute.Key, SqsSemanticHeaders.CorrelationId, StringComparison.Ordinal))
                {
                    continue;
                }

                headers[attribute.Key] = attribute.Value.StringValue ?? string.Empty;
            }
        }

        if (extra is not null)
        {
            foreach (var pair in extra)
            {
                headers[pair.Key] = pair.Value;
            }
        }

        var messageId = TryGetStringAttribute(message, SqsSemanticHeaders.MessageId);
        if (string.IsNullOrWhiteSpace(messageId))
        {
            messageId = message.MessageId;
        }

        var correlationId = TryReadCorrelationId(message);
        var body = string.IsNullOrEmpty(message.Body)
            ? Array.Empty<byte>()
            : Convert.FromBase64String(message.Body);

        return new MessageEnvelope(
            messageId,
            correlationId,
            DateTimeOffset.UtcNow,
            new ReadOnlyMemory<byte>(body),
            new ReadOnlyDictionary<string, string>(headers));
    }

    public static string? TryReadCorrelationId(Message message)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        return TryGetStringAttribute(message, SqsSemanticHeaders.CorrelationId);
    }

    private static MessageAttributeValue CreateStringAttribute(string value)
    {
        return new MessageAttributeValue
        {
            DataType = "String",
            StringValue = value,
        };
    }

    private static bool IsStringAttribute(MessageAttributeValue? value)
    {
        return value is not null &&
               string.Equals(value.DataType, "String", StringComparison.Ordinal);
    }

    private static string? TryGetStringAttribute(Message message, string key)
    {
        if (message.MessageAttributes is null ||
            !message.MessageAttributes.TryGetValue(key, out var attribute) ||
            !IsStringAttribute(attribute))
        {
            return null;
        }

        return attribute.StringValue;
    }
}
