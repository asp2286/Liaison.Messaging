using Liaison.Messaging;
using Liaison.Messaging.InMemory;

var successClient = new InMemoryRequestClient<PingRequest, string>(
    new SuccessPingHandler(),
    timeout: TimeSpan.FromSeconds(5));

var successReply = await successClient.SendAsync(new PingRequest("hello"));
Console.WriteLine($"Success reply status: {successReply.Status}");
Console.WriteLine($"Success reply value: {successReply.Value}");

var timeoutClient = new InMemoryRequestClient<PingRequest, string>(
    new TimeoutPingHandler(),
    timeout: TimeSpan.Zero);

var timeoutReply = await timeoutClient.SendAsync(new PingRequest("timeout"));
Console.WriteLine($"Timeout reply status: {timeoutReply.Status}");
Console.WriteLine($"Timeout reply error: {timeoutReply.Error}");

internal sealed record PingRequest(string Value);

internal sealed class SuccessPingHandler : IRequestHandler<PingRequest, string>
{
    public Task<string> HandleAsync(PingRequest request, MessageContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult($"pong:{request.Value}");
    }
}

internal sealed class TimeoutPingHandler : IRequestHandler<PingRequest, string>
{
    public Task<string> HandleAsync(PingRequest request, MessageContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult("never");
    }
}
