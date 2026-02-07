namespace Liaison.Messaging.Tests;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liaison.Messaging;
using Liaison.Messaging.InMemory;
using Xunit;

public class PubSubTests
{
    [Fact]
    public async Task PublishAsync_InvokesRegisteredHandler()
    {
        var pubSub = new InMemoryPubSub<TestMessage>();
        var handler = new CapturingMessageHandler<TestMessage>();
        await using var subscription = pubSub.Subscribe(handler);

        var message = new TestMessage("payload-1");
        await pubSub.PublishAsync(message);

        Assert.Single(handler.Messages);
        Assert.Equal(message, handler.Messages[0]);
        Assert.Single(handler.Contexts);
        Assert.False(string.IsNullOrWhiteSpace(handler.Contexts[0].MessageId));
    }

    [Fact]
    public async Task PublishAsync_PassesExpectedMessagePayload()
    {
        var pubSub = new InMemoryPubSub<TestMessage>();
        var handler = new CapturingMessageHandler<TestMessage>();
        await using var subscription = pubSub.Subscribe(handler);

        await pubSub.PublishAsync(new TestMessage("payload-2"));

        Assert.Single(handler.Messages);
        Assert.Equal("payload-2", handler.Messages[0].Value);
    }

    [Fact]
    public async Task PublishAsync_DoesNotInvokeDisposedSubscription()
    {
        var pubSub = new InMemoryPubSub<TestMessage>();
        var handler = new CapturingMessageHandler<TestMessage>();
        var subscription = pubSub.Subscribe(handler);

        await subscription.DisposeAsync();
        await pubSub.PublishAsync(new TestMessage("payload-3"));

        Assert.Empty(handler.Messages);
    }

    private sealed record TestMessage(string Value);

    private sealed class CapturingMessageHandler<T> : IMessageHandler<T>
    {
        public List<T> Messages { get; } = new();

        public List<MessageContext> Contexts { get; } = new();

        public Task HandleAsync(T message, MessageContext context, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            Contexts.Add(context);
            return Task.CompletedTask;
        }
    }
}
