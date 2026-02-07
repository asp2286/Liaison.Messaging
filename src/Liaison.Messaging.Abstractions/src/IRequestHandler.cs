namespace Liaison.Messaging;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Handles request messages and produces replies.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TReply">The reply type.</typeparam>
public interface IRequestHandler<TRequest, TReply>
{
    /// <summary>
    /// Handles a request.
    /// </summary>
    /// <param name="request">The request payload.</param>
    /// <param name="context">The request metadata context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The handler reply payload.</returns>
    Task<TReply> HandleAsync(TRequest request, MessageContext context, CancellationToken cancellationToken);
}
