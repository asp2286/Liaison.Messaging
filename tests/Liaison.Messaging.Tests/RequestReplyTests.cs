namespace Liaison.Messaging.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liaison.Messaging;
using Liaison.Messaging.InMemory;
using Xunit;

public class RequestReplyTests
{
    [Fact]
    public async Task SendAsync_ReturnsSuccessWhenHandlerSucceeds()
    {
        var handler = new DelegateRequestHandler<TestRequest, string>((request, _, _) =>
            Task.FromResult($"processed:{request.Value}"));

        var client = new InMemoryRequestClient<TestRequest, string>(handler);

        var reply = await client.SendAsync(new TestRequest("ok"));

        Assert.Equal(ReplyStatus.Success, reply.Status);
        Assert.Equal("processed:ok", reply.Value);
        Assert.Null(reply.Error);
    }

    [Fact]
    public async Task SendAsync_MapsArgumentExceptionToValidationError()
    {
        var handler = new DelegateRequestHandler<TestRequest, string>((_, _, _) =>
            throw new ArgumentException("invalid request"));

        var client = new InMemoryRequestClient<TestRequest, string>(handler);

        var reply = await client.SendAsync(new TestRequest("bad"));

        Assert.Equal(ReplyStatus.ValidationError, reply.Status);
        Assert.Null(reply.Value);
        Assert.Equal("invalid request", reply.Error);
    }

    [Fact]
    public async Task SendAsync_MapsGenericExceptionToFailure()
    {
        var handler = new DelegateRequestHandler<TestRequest, string>((_, _, _) =>
            throw new InvalidOperationException("boom"));

        var client = new InMemoryRequestClient<TestRequest, string>(handler);

        var reply = await client.SendAsync(new TestRequest("fail"));

        Assert.Equal(ReplyStatus.Failure, reply.Status);
        Assert.Null(reply.Value);
        Assert.Equal("boom", reply.Error);
    }

    [Fact]
    public async Task SendAsync_MapsTimeoutToTimeoutStatus()
    {
        var handler = new DelegateRequestHandler<TestRequest, string>((_, _, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult("never");
        });

        var client = new InMemoryRequestClient<TestRequest, string>(handler, TimeSpan.Zero);

        var reply = await client.SendAsync(new TestRequest("timeout"));

        Assert.Equal(ReplyStatus.Timeout, reply.Status);
        Assert.Null(reply.Value);
        Assert.False(string.IsNullOrWhiteSpace(reply.Error));
    }

    [Fact]
    public async Task SendAsync_CanceledTokenCancelsExecution()
    {
        var handler = new DelegateRequestHandler<TestRequest, string>((_, _, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult("never");
        });

        var client = new InMemoryRequestClient<TestRequest, string>(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var reply = await client.SendAsync(new TestRequest("cancel"), cts.Token);

        Assert.Equal(ReplyStatus.Timeout, reply.Status);
        Assert.Null(reply.Value);
        Assert.False(string.IsNullOrWhiteSpace(reply.Error));
    }

    private sealed record TestRequest(string Value);

    private sealed class DelegateRequestHandler<TRequest, TReply> : IRequestHandler<TRequest, TReply>
    {
        private readonly Func<TRequest, MessageContext, CancellationToken, Task<TReply>> _handler;

        public DelegateRequestHandler(Func<TRequest, MessageContext, CancellationToken, Task<TReply>> handler)
        {
            _handler = handler;
        }

        public Task<TReply> HandleAsync(TRequest request, MessageContext context, CancellationToken cancellationToken)
        {
            return _handler(request, context, cancellationToken);
        }
    }
}
