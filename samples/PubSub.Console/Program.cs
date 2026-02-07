using Liaison.Messaging;
using Liaison.Messaging.InMemory;

var pubSub = new InMemoryPubSub<OrderCreated>();
await using var subscription = pubSub.Subscribe(new OrderCreatedHandler());

var message = new OrderCreated("ORDER-1001");
await pubSub.PublishAsync(message);

Console.WriteLine("Publish completed.");

internal sealed record OrderCreated(string OrderId);

internal sealed class OrderCreatedHandler : IMessageHandler<OrderCreated>
{
    public Task HandleAsync(OrderCreated message, MessageContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Handled order message: {message.OrderId}");
        Console.WriteLine($"MessageId: {context.MessageId}");
        return Task.CompletedTask;
    }
}
