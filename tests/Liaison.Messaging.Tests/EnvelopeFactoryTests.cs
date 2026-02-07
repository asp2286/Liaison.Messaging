namespace Liaison.Messaging.Tests;

using System;
using System.Collections.Generic;
using System.Text;
using Liaison.Messaging;
using Xunit;

public class EnvelopeFactoryTests
{
    [Fact]
    public void Create_GeneratesExpectedMessageId()
    {
        var factory = new MessageEnvelopeFactory(new StubSerializer(), new StubMessageIdGenerator("id-123"));

        var envelope = factory.Create(new TestMessage("alpha"));

        Assert.Equal("id-123", envelope.MessageId);
        Assert.Equal(TimeSpan.Zero, envelope.SentAtUtc.Offset);
    }

    [Fact]
    public void Create_SerializesPayload()
    {
        var serializer = new StubSerializer();
        var factory = new MessageEnvelopeFactory(serializer, new StubMessageIdGenerator("id-456"));

        var envelope = factory.Create(new TestMessage("beta"));

        Assert.Equal("payload:TestMessage:beta", Encoding.UTF8.GetString(envelope.Body.Span));
    }

    [Fact]
    public void Create_CopiesHeadersDefensively()
    {
        var factory = new MessageEnvelopeFactory(new StubSerializer(), new StubMessageIdGenerator("id-789"));
        var headers = new Dictionary<string, string>
        {
            ["k1"] = "v1",
        };

        var envelope = factory.Create(new TestMessage("gamma"), headers: headers);

        headers["k1"] = "mutated";
        headers["k2"] = "v2";

        Assert.Equal("v1", envelope.Headers["k1"]);
        Assert.False(envelope.Headers.ContainsKey("k2"));
    }

    [Fact]
    public void Create_UsesProvidedCorrelationId()
    {
        var factory = new MessageEnvelopeFactory(new StubSerializer(), new StubMessageIdGenerator("id-999"));

        var envelope = factory.Create(new TestMessage("delta"), correlationId: "corr-42");

        Assert.Equal("corr-42", envelope.CorrelationId);
    }

    [Fact]
    public void Create_ConvertsProvidedTimestampToUtc()
    {
        var factory = new MessageEnvelopeFactory(new StubSerializer(), new StubMessageIdGenerator("id-utc"));
        var sentAt = new DateTimeOffset(2026, 2, 7, 10, 15, 30, TimeSpan.FromHours(2));

        var envelope = factory.Create(new TestMessage("epsilon"), sentAtUtc: sentAt);

        Assert.Equal(sentAt.ToUniversalTime(), envelope.SentAtUtc);
        Assert.Equal(TimeSpan.Zero, envelope.SentAtUtc.Offset);
    }

    private sealed record TestMessage(string Value);

    private sealed class StubMessageIdGenerator : IMessageIdGenerator
    {
        private readonly string _value;

        public StubMessageIdGenerator(string value)
        {
            _value = value;
        }

        public string NewId() => _value;
    }

    private sealed class StubSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value)
        {
            var payloadValue = value is TestMessage message
                ? message.Value
                : value?.ToString() ?? "null";
            return Encoding.UTF8.GetBytes($"payload:{typeof(T).Name}:{payloadValue}");
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> payload)
        {
            throw new NotSupportedException();
        }
    }
}
