namespace Liaison.Messaging.Tests;

using System;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class LiaisonMessagingDefaultsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLiaisonMessagingDefaults_ResolvesIMessageSerializerAsSystemTextJsonMessageSerializer()
    {
        var services = new ServiceCollection();
        services.AddLiaisonMessagingDefaults();

        using var provider = services.BuildServiceProvider();

        var serializer = provider.GetRequiredService<IMessageSerializer>();

        Assert.IsType<SystemTextJsonMessageSerializer>(serializer);
    }

    [Fact]
    public void AddLiaisonMessagingDefaults_ResolvesIMessageIdGeneratorAsGuidMessageIdGenerator()
    {
        var services = new ServiceCollection();
        services.AddLiaisonMessagingDefaults();

        using var provider = services.BuildServiceProvider();

        var messageIdGenerator = provider.GetRequiredService<IMessageIdGenerator>();

        Assert.IsType<GuidMessageIdGenerator>(messageIdGenerator);
    }

    [Fact]
    public void AddLiaisonMessagingDefaults_ResolvesIMessageEnvelopeFactoryAsMessageEnvelopeFactory()
    {
        var services = new ServiceCollection();
        services.AddLiaisonMessagingDefaults();

        using var provider = services.BuildServiceProvider();

        var envelopeFactory = provider.GetRequiredService<IMessageEnvelopeFactory>();

        Assert.IsType<MessageEnvelopeFactory>(envelopeFactory);
    }

    [Fact]
    public void AddLiaisonMessagingDefaults_ResolvesIMessageContextFactoryAsMessageContextFactory()
    {
        var services = new ServiceCollection();
        services.AddLiaisonMessagingDefaults();

        using var provider = services.BuildServiceProvider();

        var contextFactory = provider.GetRequiredService<IMessageContextFactory>();

        Assert.IsType<MessageContextFactory>(contextFactory);
    }

    [Fact]
    public void AddLiaisonMessagingDefaults_DoesNotOverwriteExistingIMessageSerializerRegistration()
    {
        var services = new ServiceCollection();
        var existingSerializer = new ExistingSerializer();
        services.AddSingleton<IMessageSerializer>(existingSerializer);

        services.AddLiaisonMessagingDefaults();

        using var provider = services.BuildServiceProvider();

        var serializer = provider.GetRequiredService<IMessageSerializer>();

        Assert.Same(existingSerializer, serializer);
    }

    [Fact]
    public void AddLiaisonMessagingDefaults_CanBeCalledTwice()
    {
        var services = new ServiceCollection();

        services.AddLiaisonMessagingDefaults();
        services.AddLiaisonMessagingDefaults();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<SystemTextJsonMessageSerializer>(provider.GetRequiredService<IMessageSerializer>());
    }

    [Fact]
    public void AddLiaisonMessagingDefaults_NullServices_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => LiaisonMessagingDefaultsServiceCollectionExtensions.AddLiaisonMessagingDefaults(null!));

        Assert.Equal("services", exception.ParamName);
    }

    private sealed class ExistingSerializer : IMessageSerializer
    {
        public T Deserialize<T>(ReadOnlyMemory<byte> payload)
        {
            throw new NotSupportedException();
        }

        public byte[] Serialize<T>(T value)
        {
            throw new NotSupportedException();
        }
    }
}
