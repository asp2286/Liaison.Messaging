namespace Liaison.Messaging;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Serializes message payloads with <see cref="JsonSerializer"/> using deterministic defaults.
/// </summary>
public sealed class SystemTextJsonMessageSerializer : IMessageSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    /// <summary>
    /// Serializes the provided value into a UTF-8 JSON payload.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The serialized payload.</returns>
    public byte[] Serialize<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
    }

    /// <summary>
    /// Deserializes the provided UTF-8 JSON payload into a value.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="payload">The payload to deserialize.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="InvalidOperationException">Thrown when deserialization returns <see langword="null"/>.</exception>
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
