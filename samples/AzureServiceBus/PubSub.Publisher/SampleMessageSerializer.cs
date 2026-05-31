using Liaison.Messaging;

internal sealed class SampleMessageSerializer : IMessageSerializer
{
    public byte[] Serialize<T>(T value) =>
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);

    public T Deserialize<T>(ReadOnlyMemory<byte> payload) =>
        System.Text.Json.JsonSerializer.Deserialize<T>(payload.Span)!;
}
