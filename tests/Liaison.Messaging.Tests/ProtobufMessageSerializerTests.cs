namespace Liaison.Messaging.Tests;

using System;
using Liaison.Messaging.Serialization.Protobuf.Demo;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class ProtobufMessageSerializerTests
{
    [Fact]
    public void SerializeAndDeserialize_ProtobufMessage_RoundTripsFields()
    {
        var serializer = new ProtobufMessageSerializer();
        var message = new DemoUserRegistered
        {
            UserId = "user-123",
            Email = "user@example.com",
            RegisteredAtUnixMs = 1_774_885_600_000,
        };

        var payload = serializer.Serialize(message);
        var roundTripped = serializer.Deserialize<DemoUserRegistered>(payload);

        Assert.Equal(message.UserId, roundTripped.UserId);
        Assert.Equal(message.Email, roundTripped.Email);
        Assert.Equal(message.RegisteredAtUnixMs, roundTripped.RegisteredAtUnixMs);
    }

    [Fact]
    public void SerializeAndDeserialize_DefaultProtobufMessage_RoundTripsDefaults()
    {
        var serializer = new ProtobufMessageSerializer();
        var message = new DemoUserRegistered();

        var payload = serializer.Serialize(message);
        var roundTripped = serializer.Deserialize<DemoUserRegistered>(payload);

        Assert.Empty(payload);
        Assert.Equal(string.Empty, roundTripped.UserId);
        Assert.Equal(string.Empty, roundTripped.Email);
        Assert.Equal(0, roundTripped.RegisteredAtUnixMs);
    }

    [Fact]
    public void Serialize_NonProtobufType_ThrowsInvalidOperationException()
    {
        var serializer = new ProtobufMessageSerializer();
        var value = new Foo("bar");

        var exception = Assert.Throws<InvalidOperationException>(() => serializer.Serialize(value));

        Assert.Contains("ProtobufMessageSerializer requires a Google.Protobuf message type.", exception.Message);
        Assert.Contains(typeof(Foo).FullName!, exception.Message);
        Assert.Contains("does not implement IMessage", exception.Message);
    }

    [Fact]
    public void Deserialize_NonProtobufType_ThrowsInvalidOperationException()
    {
        var serializer = new ProtobufMessageSerializer();

        var exception = Assert.Throws<InvalidOperationException>(
            () => serializer.Deserialize<Foo>(ReadOnlyMemory<byte>.Empty));

        Assert.Contains("ProtobufMessageSerializer requires a Google.Protobuf message type.", exception.Message);
        Assert.Contains(typeof(Foo).FullName!, exception.Message);
        Assert.Contains("does not implement IMessage", exception.Message);
    }

    [Fact]
    public void Deserialize_SameProtobufTypeTwice_UsesCachedParserPath()
    {
        var serializer = new ProtobufMessageSerializer();
        var firstMessage = new DemoUserRegistered
        {
            UserId = "user-1",
            Email = "one@example.com",
            RegisteredAtUnixMs = 1,
        };
        var secondMessage = new DemoUserRegistered
        {
            UserId = "user-2",
            Email = "two@example.com",
            RegisteredAtUnixMs = 2,
        };

        var firstPayload = serializer.Serialize(firstMessage);
        var firstRoundTrip = serializer.Deserialize<DemoUserRegistered>(firstPayload);
        var secondPayload = serializer.Serialize(secondMessage);
        var secondRoundTrip = serializer.Deserialize<DemoUserRegistered>(secondPayload);

        Assert.Equal("user-1", firstRoundTrip.UserId);
        Assert.Equal("one@example.com", firstRoundTrip.Email);
        Assert.Equal(1, firstRoundTrip.RegisteredAtUnixMs);
        Assert.Equal("user-2", secondRoundTrip.UserId);
        Assert.Equal("two@example.com", secondRoundTrip.Email);
        Assert.Equal(2, secondRoundTrip.RegisteredAtUnixMs);
    }

    [Fact]
    public void AddProtobufMessageSerializer_RegistersIMessageSerializerAsProtobufMessageSerializer()
    {
        var services = new ServiceCollection();
        services.AddProtobufMessageSerializer();

        using var provider = services.BuildServiceProvider();

        var serializer = provider.GetRequiredService<IMessageSerializer>();

        Assert.IsType<ProtobufMessageSerializer>(serializer);
    }

    [Fact]
    public void AddProtobufMessageSerializer_DoesNotOverwriteExistingIMessageSerializerRegistration()
    {
        var services = new ServiceCollection();
        var existingSerializer = new ExistingSerializer();
        services.AddSingleton<IMessageSerializer>(existingSerializer);

        services.AddProtobufMessageSerializer();

        using var provider = services.BuildServiceProvider();

        var serializer = provider.GetRequiredService<IMessageSerializer>();

        Assert.Same(existingSerializer, serializer);
    }

    [Fact]
    public void Serialize_ProducesStandardProtobufWireFormat()
    {
        var serializer = new ProtobufMessageSerializer();
        var message = new DemoUserRegistered
        {
            UserId = "user-123",
            Email = "user@example.com",
            RegisteredAtUnixMs = 1_774_885_600_000,
        };

        var payload = serializer.Serialize(message);
        var parsed = DemoUserRegistered.Parser.ParseFrom(payload);

        Assert.Equal(message.UserId, parsed.UserId);
        Assert.Equal(message.Email, parsed.Email);
        Assert.Equal(message.RegisteredAtUnixMs, parsed.RegisteredAtUnixMs);
    }

    private sealed record Foo(string X);

    private sealed class ExistingSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value)
        {
            throw new NotSupportedException();
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> payload)
        {
            throw new NotSupportedException();
        }
    }
}
