namespace Liaison.Messaging.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Liaison.Messaging;
using Liaison.Messaging.AzureServiceBus;
using Xunit;

/// <summary>
/// Verifies that the optional <see cref="ILargePayloadPolicy"/> / <see cref="IPayloadStore"/>
/// integration in the Azure Service Bus transport components works correctly.
/// </summary>
public sealed class AzureServiceBusLargePayloadIntegrationTests
{
    // ---------------------------------------------------------------
    // Publisher tests
    // ---------------------------------------------------------------

    [Fact]
    public async Task PublishAsync_WithPolicy_CallsPrepareOutboundAsync()
    {
        var sender = new FakeServiceBusSender();
        var client = new FakeServiceBusClient(sender);
        var policy = new CapturingLargePayloadPolicy();
        var store = new NoOpPayloadStore();
        var envelopeFactory = new StubEnvelopeFactory();
        var entityOptions = new AzureServiceBusEntityOptions("test-queue", AzureServiceBusEntityKind.Queue);

        var publisher = new AzureServiceBusPublisher<string>(
            client, envelopeFactory, entityOptions, largePayloadPolicy: policy, payloadStore: store);

        await publisher.PublishAsync("hello");

        Assert.Equal(1, policy.PrepareOutboundCallCount);
        Assert.Equal(0, policy.ResolveInboundCallCount);
        Assert.Equal(1, sender.SendCount);
    }

    [Fact]
    public async Task PublishAsync_WithoutPolicy_SendsWithoutPolicyCall()
    {
        var sender = new FakeServiceBusSender();
        var client = new FakeServiceBusClient(sender);
        var envelopeFactory = new StubEnvelopeFactory();
        var entityOptions = new AzureServiceBusEntityOptions("test-queue", AzureServiceBusEntityKind.Queue);

        var publisher = new AzureServiceBusPublisher<string>(
            client, envelopeFactory, entityOptions);

        await publisher.PublishAsync("hello");

        Assert.Equal(1, sender.SendCount);
    }

    [Fact]
    public async Task PublishAsync_WithPolicyButNullStore_ThrowsInvalidOperationException()
    {
        var sender = new FakeServiceBusSender();
        var client = new FakeServiceBusClient(sender);
        var policy = new CapturingLargePayloadPolicy();
        var envelopeFactory = new StubEnvelopeFactory();
        var entityOptions = new AzureServiceBusEntityOptions("test-queue", AzureServiceBusEntityKind.Queue);

        var publisher = new AzureServiceBusPublisher<string>(
            client, envelopeFactory, entityOptions, largePayloadPolicy: policy, payloadStore: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync("hello"));
        Assert.Contains("IPayloadStore", ex.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------
    // Subscription inbound policy test
    // ---------------------------------------------------------------

    [Fact]
    public async Task Subscription_ApplyInboundPolicyAsync_CallsResolveInboundAsync()
    {
        var policy = new CapturingLargePayloadPolicy();
        var store = new NoOpPayloadStore();
        var sender = new FakeServiceBusSender();
        var client = new FakeServiceBusClient(sender);
        var serializer = new StubSerializer();
        var contextFactory = new StubContextFactory();
        var handler = new StubMessageHandler();
        var entityOptions = new AzureServiceBusEntityOptions("test-queue", AzureServiceBusEntityKind.Queue);

        var subscription = new AzureServiceBusSubscription<string>(
            client, serializer, contextFactory, entityOptions, handler,
            logger: null, largePayloadPolicy: policy, payloadStore: store);

        var envelope = CreateTestEnvelope();

        // ApplyInboundPolicyAsync is private; invoke via reflection to verify
        // the policy integration without requiring a live Service Bus connection.
        var method = typeof(AzureServiceBusSubscription<string>)
            .GetMethod("ApplyInboundPolicyAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var resultTask = (Task<MessageEnvelope>)method.Invoke(
            subscription, new object[] { envelope, CancellationToken.None })!;
        var result = await resultTask;

        Assert.Equal(1, policy.ResolveInboundCallCount);
        Assert.Equal(0, policy.PrepareOutboundCallCount);
        Assert.Same(envelope, result);

        await subscription.DisposeAsync();
    }

    // ---------------------------------------------------------------
    // RequestProcessor reply outbound policy test
    // ---------------------------------------------------------------

    [Fact]
    public async Task RequestProcessor_SendReplyAsync_CallsPrepareOutboundAsync()
    {
        var policy = new CapturingLargePayloadPolicy();
        var store = new NoOpPayloadStore();
        var sender = new FakeServiceBusSender();
        var client = new FakeServiceBusClient(sender);
        var serializer = new StubSerializer();
        var contextFactory = new StubContextFactory();
        var handler = new StubRequestHandler();
        var options = new AzureServiceBusRequestReplyOptions("request-queue", "reply-queue", TimeSpan.FromSeconds(30));

        var processor = new AzureServiceBusRequestProcessor<string, string>(
            client, serializer, contextFactory, options, handler,
            logger: null, largePayloadPolicy: policy, payloadStore: store);

        // SendReplyAsync is private; invoke via reflection to verify the
        // outbound policy integration on the reply path.
        var method = typeof(AzureServiceBusRequestProcessor<string, string>)
            .GetMethod("SendReplyAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var resultTask = (Task)method.Invoke(
            processor, new object?[] { "reply-value", "corr-1", null, null, CancellationToken.None })!;
        await resultTask;

        Assert.Equal(1, policy.PrepareOutboundCallCount);
        Assert.Equal(0, policy.ResolveInboundCallCount);
        Assert.Equal(1, sender.SendCount);

        await processor.DisposeAsync();
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static MessageEnvelope CreateTestEnvelope()
    {
        return new MessageEnvelope(
            messageId: "test-id",
            correlationId: "test-corr",
            sentAtUtc: DateTimeOffset.UtcNow,
            body: new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 }),
            headers: new Dictionary<string, string>());
    }

    // ---------------------------------------------------------------
    // Fakes – Azure Service Bus SDK types
    // ---------------------------------------------------------------

    private sealed class FakeServiceBusSender : ServiceBusSender
    {
        public int SendCount { get; private set; }
        public ServiceBusMessage? LastMessage { get; private set; }

        public override Task SendMessageAsync(
            ServiceBusMessage message,
            CancellationToken cancellationToken = default)
        {
            SendCount++;
            LastMessage = message;
            return Task.CompletedTask;
        }

        public override Task CloseAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeServiceBusProcessor : ServiceBusProcessor
    {
        public override Task StartProcessingAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public override Task StopProcessingAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public override Task CloseAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeServiceBusClient : ServiceBusClient
    {
        private readonly FakeServiceBusSender _sender;

        public FakeServiceBusClient(FakeServiceBusSender sender)
        {
            _sender = sender;
        }

        public override ServiceBusSender CreateSender(string queueOrTopicName) => _sender;

        public override ServiceBusProcessor CreateProcessor(
            string queueName,
            ServiceBusProcessorOptions? options) => new FakeServiceBusProcessor();
    }

    // ---------------------------------------------------------------
    // Fakes – Liaison.Messaging abstractions
    // ---------------------------------------------------------------

    private sealed class CapturingLargePayloadPolicy : ILargePayloadPolicy
    {
        public int ThresholdBytes => int.MaxValue;
        public bool UseCompression => false;
        public int PrepareOutboundCallCount { get; private set; }
        public int ResolveInboundCallCount { get; private set; }
        public MessageEnvelope? LastPreparedEnvelope { get; private set; }
        public MessageEnvelope? LastResolvedEnvelope { get; private set; }

        public Task<MessageEnvelope> PrepareOutboundAsync(
            MessageEnvelope envelope,
            IPayloadStore store,
            DateTimeOffset? expiresAtUtc = null,
            CancellationToken ct = default)
        {
            PrepareOutboundCallCount++;
            LastPreparedEnvelope = envelope;
            return Task.FromResult(envelope);
        }

        public Task<MessageEnvelope> ResolveInboundAsync(
            MessageEnvelope envelope,
            IPayloadStore store,
            CancellationToken ct = default)
        {
            ResolveInboundCallCount++;
            LastResolvedEnvelope = envelope;
            return Task.FromResult(envelope);
        }
    }

    private sealed class NoOpPayloadStore : IPayloadStore
    {
        public Task<string> UploadAsync(
            Stream payload,
            string keyPrefix,
            long? sizeHintBytes = null,
            DateTimeOffset? expiresAtUtc = null,
            CancellationToken ct = default)
            => Task.FromResult("ref-noop");

        public Task<Stream> DownloadAsync(string reference, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(string reference, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class StubEnvelopeFactory : IMessageEnvelopeFactory
    {
        public MessageEnvelope Create<T>(
            T message,
            IReadOnlyDictionary<string, string>? headers = null,
            string? correlationId = null,
            DateTimeOffset? sentAtUtc = null)
        {
            return new MessageEnvelope(
                messageId: Guid.NewGuid().ToString("N"),
                correlationId: correlationId,
                sentAtUtc: sentAtUtc ?? DateTimeOffset.UtcNow,
                body: new ReadOnlyMemory<byte>(
                    System.Text.Encoding.UTF8.GetBytes(message?.ToString() ?? string.Empty)),
                headers: headers ?? new Dictionary<string, string>());
        }
    }

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
