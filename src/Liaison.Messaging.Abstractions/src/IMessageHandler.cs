namespace Liaison.Messaging;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Handles published messages.
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
public interface IMessageHandler<T>
{
    /// <summary>
    /// Handles a message.
    /// </summary>
    /// <param name="message">The message payload.</param>
    /// <param name="context">The message metadata context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when handling finishes.</returns>
    Task HandleAsync(T message, MessageContext context, CancellationToken cancellationToken);
}
