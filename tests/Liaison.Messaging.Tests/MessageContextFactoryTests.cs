namespace Liaison.Messaging.Tests;

using System;
using System.Collections.Generic;
using Liaison.Messaging;
using Xunit;

public class MessageContextFactoryTests
{
    [Fact]
    public void Create_MapsEnvelopeValuesWithoutTransformation()
    {
        var headers = new Dictionary<string, string>
        {
            ["h1"] = "v1",
            ["h2"] = "v2",
        };

        var envelope = new MessageEnvelope(
            messageId: "msg-1",
            correlationId: "corr-1",
            sentAtUtc: new DateTimeOffset(2026, 2, 7, 0, 0, 0, TimeSpan.Zero),
            body: new byte[] { 1, 2, 3 },
            headers: headers);

        var factory = new MessageContextFactory();

        var context = factory.Create(envelope);

        Assert.Equal(envelope.MessageId, context.MessageId);
        Assert.Equal(envelope.CorrelationId, context.CorrelationId);
        Assert.Equal(envelope.Headers.Count, context.Headers.Count);
        Assert.Equal("v1", context.Headers["h1"]);
        Assert.Equal("v2", context.Headers["h2"]);
    }
}
