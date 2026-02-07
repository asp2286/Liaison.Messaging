namespace Liaison.Messaging;

/// <summary>
/// Creates handler-facing message contexts.
/// </summary>
public interface IMessageContextFactory
{
    /// <summary>
    /// Creates a context from an existing message envelope.
    /// </summary>
    /// <param name="envelope">The source message envelope.</param>
    /// <returns>The resulting message context.</returns>
    MessageContext Create(MessageEnvelope envelope);
}
