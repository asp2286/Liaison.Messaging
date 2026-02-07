namespace Liaison.Messaging.InMemory;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Liaison.Messaging;

internal sealed class SystemTextJsonMessageSerializer : IMessageSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    public byte[] Serialize<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
    }

    public T Deserialize<T>(ReadOnlyMemory<byte> payload)
    {
        var value = JsonSerializer.Deserialize<T>(payload.Span, SerializerOptions);

        if (value is null)
        {
            throw new InvalidOperationException("Deserialized payload was null.");
        }

        return value;
    }
}
