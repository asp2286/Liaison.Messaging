namespace Liaison.Messaging;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Sends requests and receives replies.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TReply">The reply type.</typeparam>
public interface IRequestClient<TRequest, TReply>
{
    /// <summary>
    /// Sends a request and awaits a reply.
    /// </summary>
    /// <param name="request">The request payload.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A transport-agnostic reply result.</returns>
    Task<Reply<TReply>> SendAsync(TRequest request, CancellationToken cancellationToken = default);
}
