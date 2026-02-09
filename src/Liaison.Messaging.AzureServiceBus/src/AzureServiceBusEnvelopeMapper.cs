namespace Liaison.Messaging.AzureServiceBus;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Azure.Messaging.ServiceBus;
using Liaison.Messaging;

internal static class AzureServiceBusEnvelopeMapper
{
    public static ServiceBusMessage ToServiceBusMessage(MessageEnvelope envelope)
    {
        if (envelope is null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        var body = BinaryData.FromBytes(envelope.Body.ToArray());
        var mapped = new ServiceBusMessage(body)
        {
            MessageId = envelope.MessageId,
        };

        if (envelope.CorrelationId is not null)
        {
            mapped.CorrelationId = envelope.CorrelationId;
        }

        foreach (var pair in envelope.Headers)
        {
            mapped.ApplicationProperties[pair.Key] = pair.Value;
        }

        if (envelope.Headers.TryGetValue("content-type", out var contentType) && !string.IsNullOrWhiteSpace(contentType))
        {
            mapped.ContentType = contentType;
        }

        return mapped;
    }

    public static MessageEnvelope FromServiceBusReceivedMessage(
        ServiceBusReceivedMessage message,
        IReadOnlyDictionary<string, string>? extraHeaders = null)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in message.ApplicationProperties)
        {
            if (property.Value is string value)
            {
                headers[property.Key] = value;
            }
        }

        if (extraHeaders is not null)
        {
            foreach (var pair in extraHeaders)
            {
                headers[pair.Key] = pair.Value;
            }
        }

        var messageId = string.IsNullOrWhiteSpace(message.MessageId)
            ? message.LockToken
            : message.MessageId;
        var sentAtUtc = message.EnqueuedTime == default
            ? DateTimeOffset.UtcNow
            : message.EnqueuedTime.ToUniversalTime();

        return new MessageEnvelope(
            messageId,
            message.CorrelationId,
            sentAtUtc,
            message.Body.ToArray(),
            new ReadOnlyDictionary<string, string>(headers));
    }
}
