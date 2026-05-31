using Liaison.Messaging;
using Sample.Contracts;

public sealed class UserRegisteredHandler : IMessageHandler<UserRegistered>
{
    public Task HandleAsync(
        UserRegistered message,
        MessageContext context,
        CancellationToken cancellationToken)
    {
        Console.WriteLine(
            $"[Subscriber] Received UserRegistered: UserId={message.UserId} Email={message.Email}");
        return Task.CompletedTask;
    }
}
