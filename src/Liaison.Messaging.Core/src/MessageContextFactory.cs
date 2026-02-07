namespace Liaison.Messaging;

using System;

/// <summary>
/// Maps <see cref="MessageEnvelope"/> values to <see cref="MessageContext"/> values.
/// </summary>
public sealed class MessageContextFactory : IMessageContextFactory
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="envelope"/> is <see langword="null"/>.</exception>
    public MessageContext Create(MessageEnvelope envelope)
    {
        if (envelope is null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        return new MessageContext(envelope.MessageId, envelope.CorrelationId, envelope.Headers);
    }
}
