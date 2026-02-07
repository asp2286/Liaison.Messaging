namespace Liaison.Messaging.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liaison.Messaging;
using Liaison.Messaging.InMemory;
using Xunit;

public class TimeoutPolicyTests
{
    [Fact]
    public void FixedRequestTimeoutPolicy_ReturnsConfiguredTimeout()
    {
        var policy = new FixedRequestTimeoutPolicy(TimeSpan.FromSeconds(12));

        var timeout = policy.GetTimeout();

        Assert.Equal(TimeSpan.FromSeconds(12), timeout);
    }

    [Fact]
    public async Task InMemoryRequestClient_RespectsTimeoutPolicy()
    {
        var policy = new RecordingTimeoutPolicy(TimeSpan.Zero);
        var handler = new DelegateRequestHandler<TestRequest, string>((_, _, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult("never");
        });

        var client = new InMemoryRequestClient<TestRequest, string>(handler, policy);

        var reply = await client.SendAsync(new TestRequest("timeout"));

        Assert.Equal(1, policy.CallCount);
        Assert.Equal(ReplyStatus.Timeout, reply.Status);
    }

    private sealed record TestRequest(string Value);

    private sealed class RecordingTimeoutPolicy : IRequestTimeoutPolicy
    {
        private readonly TimeSpan _timeout;

        public RecordingTimeoutPolicy(TimeSpan timeout)
        {
            _timeout = timeout;
        }

        public int CallCount { get; private set; }

        public TimeSpan GetTimeout(IReadOnlyDictionary<string, string>? headers = null)
        {
            CallCount++;
            return _timeout;
        }
    }

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
