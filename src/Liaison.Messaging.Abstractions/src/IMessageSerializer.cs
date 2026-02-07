namespace Liaison.Messaging;

using System;

/// <summary>
/// Serializes and deserializes message payloads.
/// </summary>
public interface IMessageSerializer
{
    /// <summary>
    /// Serializes the provided value into a binary payload.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The serialized payload.</returns>
    byte[] Serialize<T>(T value);

    /// <summary>
    /// Deserializes the provided payload into a value.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="payload">The payload to deserialize.</param>
    /// <returns>The deserialized value.</returns>
    T Deserialize<T>(ReadOnlyMemory<byte> payload);
}
