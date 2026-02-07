namespace Liaison.Messaging;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Publishes messages to subscribers.
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
public interface IMessagePublisher<T>
{
    /// <summary>
    /// Publishes a message.
    /// </summary>
    /// <param name="message">The message to publish.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when publishing is finished.</returns>
    Task PublishAsync(T message, CancellationToken cancellationToken = default);
}
