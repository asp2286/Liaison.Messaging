namespace Liaison.Messaging.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Liaison.Messaging;
using Liaison.Messaging.AzureServiceBus;
using Liaison.Messaging.Hosting;
using Xunit;

/// <summary>
/// Tests for <see cref="AzureServiceBusSubscriptionService{T}"/> and
/// <see cref="AzureServiceBusRequestProcessorService{TRequest, TReply}"/>.
/// </summary>
public sealed class HostedServiceTests
{
    // ---------------------------------------------------------------
    // AzureServiceBusSubscriptionService<T>
    // ---------------------------------------------------------------

    [Fact]
    public async Task SubscriptionService_StartAsync_DelegatesToSubscription()
    {
        var tracker = new TrackingServiceBusProcessor();
        var client = new TrackingServiceBusClient(tracker);
        var subscription = CreateSubscription(client);
        var service = new AzureServiceBusSubscriptionService<string>(subscription);
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);

        Assert.Equal(1, tracker.StartProcessingCallCount);
        Assert.Equal(cts.Token, tracker.LastStartCancellationToken);

        await service.DisposeAsync();
    }

    [Fact]
    public async Task SubscriptionService_StopAsync_DisposesSubscription()
    {
        var tracker = new TrackingServiceBusProcessor();
        var client = new TrackingServiceBusClient(tracker);
        var subscription = CreateSubscription(client);
        var service = new AzureServiceBusSubscriptionService<string>(subscription);

        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, tracker.StopProcessingCallCount);
    }

    [Fact]
    public async Task SubscriptionService_DisposeAsync_DisposesSubscription()
    {
        var tracker = new TrackingServiceBusProcessor();
        var client = new TrackingServiceBusClient(tracker);
        var subscription = CreateSubscription(client);
        var service = new AzureServiceBusSubscriptionService<string>(subscription);

        await service.DisposeAsync();

        Assert.Equal(1, tracker.StopProcessingCallCount);
        Assert.Equal(1, tracker.CloseCallCount);
    }

    [Fact]
    public void SubscriptionService_Constructor_ThrowsWhenNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AzureServiceBusSubscriptionService<string>(null!));
    }

    // ---------------------------------------------------------------
    // AzureServiceBusRequestProcessorService<TRequest, TReply>
    // ---------------------------------------------------------------

    [Fact]
    public async Task ProcessorService_StartAsync_DelegatesToProcessor()
    {
        var tracker = new TrackingServiceBusProcessor();
        var client = new TrackingServiceBusClient(tracker);
        var processor = CreateRequestProcessor(client);
        var service = new AzureServiceBusRequestProcessorService<string, string>(processor);
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);

        Assert.Equal(1, tracker.StartProcessingCallCount);
        Assert.Equal(cts.Token, tracker.LastStartCancellationToken);

        await service.DisposeAsync();
    }

    [Fact]
    public async Task ProcessorService_StopAsync_DisposesProcessor()
    {
        var tracker = new TrackingServiceBusProcessor();
        var client = new TrackingServiceBusClient(tracker);
        var processor = CreateRequestProcessor(client);
        var service = new AzureServiceBusRequestProcessorService<string, string>(processor);

        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, tracker.StopProcessingCallCount);
    }

    [Fact]
    public async Task ProcessorService_DisposeAsync_DisposesProcessor()
    {
        var tracker = new TrackingServiceBusProcessor();
        var client = new TrackingServiceBusClient(tracker);
        var processor = CreateRequestProcessor(client);
        var service = new AzureServiceBusRequestProcessorService<string, string>(processor);

        await service.DisposeAsync();

        Assert.Equal(1, tracker.StopProcessingCallCount);
        Assert.Equal(1, tracker.CloseCallCount);
    }

    [Fact]
    public void ProcessorService_Constructor_ThrowsWhenNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AzureServiceBusRequestProcessorService<string, string>(null!));
    }

    // ---------------------------------------------------------------
    // Factory helpers
    // ---------------------------------------------------------------

    private static AzureServiceBusSubscription<string> CreateSubscription(TrackingServiceBusClient client)
    {
        return new AzureServiceBusSubscription<string>(
            client,
            new StubSerializer(),
            new StubContextFactory(),
            new AzureServiceBusEntityOptions("test-queue", AzureServiceBusEntityKind.Queue),
            new StubMessageHandler());
    }

    private static AzureServiceBusRequestProcessor<string, string> CreateRequestProcessor(TrackingServiceBusClient client)
    {
        return new AzureServiceBusRequestProcessor<string, string>(
            client,
            new StubSerializer(),
            new StubContextFactory(),
            new AzureServiceBusRequestReplyOptions("request-queue", "reply-queue", TimeSpan.FromSeconds(30)),
            new StubRequestHandler());
    }

    // ---------------------------------------------------------------
    // Fakes — Azure Service Bus SDK types with call tracking
    // ---------------------------------------------------------------

    private sealed class TrackingServiceBusProcessor : ServiceBusProcessor
    {
        public int StartProcessingCallCount { get; private set; }
        public int StopProcessingCallCount { get; private set; }
        public int CloseCallCount { get; private set; }
        public CancellationToken LastStartCancellationToken { get; private set; }

        public override Task StartProcessingAsync(CancellationToken cancellationToken = default)
        {
            StartProcessingCallCount++;
            LastStartCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        public override Task StopProcessingAsync(CancellationToken cancellationToken = default)
        {
            StopProcessingCallCount++;
            return Task.CompletedTask;
        }

        public override Task CloseAsync(CancellationToken cancellationToken = default)
        {
            CloseCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingServiceBusSender : ServiceBusSender
    {
        public override Task SendMessageAsync(
            ServiceBusMessage message,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public override Task CloseAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class TrackingServiceBusClient : ServiceBusClient
    {
        private readonly TrackingServiceBusProcessor _processor;

        public TrackingServiceBusClient(TrackingServiceBusProcessor processor)
        {
            _processor = processor;
        }

        public override ServiceBusSender CreateSender(string queueOrTopicName)
            => new TrackingServiceBusSender();

        public override ServiceBusProcessor CreateProcessor(
            string queueName,
            ServiceBusProcessorOptions? options)
            => _processor;
    }

    // ---------------------------------------------------------------
    // Fakes — Liaison.Messaging abstractions
    // ---------------------------------------------------------------

    private sealed class StubSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value)
            => System.Text.Encoding.UTF8.GetBytes(value?.ToString() ?? string.Empty);

        public T Deserialize<T>(ReadOnlyMemory<byte> payload)
            => (T)(object)System.Text.Encoding.UTF8.GetString(payload.Span);
    }

    private sealed class StubContextFactory : IMessageContextFactory
    {
        public MessageContext Create(MessageEnvelope envelope)
            => new MessageContext(envelope.MessageId, envelope.CorrelationId, envelope.Headers);
    }

    private sealed class StubMessageHandler : IMessageHandler<string>
    {
        public Task HandleAsync(string message, MessageContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubRequestHandler : IRequestHandler<string, string>
    {
        public Task<string> HandleAsync(string request, MessageContext context, CancellationToken cancellationToken)
            => Task.FromResult("reply");
    }
}
