namespace Liaison.Messaging.InMemory;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liaison.Messaging;

/// <summary>
/// Provides an in-memory request/reply client implementation.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TReply">The reply type.</typeparam>
public sealed class InMemoryRequestClient<TRequest, TReply> : IRequestClient<TRequest, TReply>
{
    private readonly IRequestHandler<TRequest, TReply> _handler;
    private readonly IRequestTimeoutPolicy _timeoutPolicy;
    private readonly IMessageEnvelopeFactory _envelopeFactory;
    private readonly IMessageContextFactory _contextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryRequestClient{TRequest, TReply}"/> type.
    /// </summary>
    /// <param name="handler">The single handler that serves requests.</param>
    /// <param name="timeout">The optional timeout for each request.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="handler"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeout"/> is negative.</exception>
    public InMemoryRequestClient(IRequestHandler<TRequest, TReply> handler, TimeSpan? timeout = null)
        : this(
            handler,
            timeoutPolicy: timeout.HasValue
                ? new FixedRequestTimeoutPolicy(timeout.Value)
                : new FixedRequestTimeoutPolicy(Timeout.InfiniteTimeSpan))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryRequestClient{TRequest, TReply}"/> type.
    /// </summary>
    /// <param name="handler">The single handler that serves requests.</param>
    /// <param name="timeoutPolicy">The timeout policy applied to each request.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="handler"/> or <paramref name="timeoutPolicy"/> is <see langword="null"/>.
    /// </exception>
    public InMemoryRequestClient(IRequestHandler<TRequest, TReply> handler, IRequestTimeoutPolicy timeoutPolicy)
        : this(
            handler,
            timeoutPolicy,
            new MessageEnvelopeFactory(new SystemTextJsonMessageSerializer(), new GuidMessageIdGenerator()),
            new MessageContextFactory())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryRequestClient{TRequest, TReply}"/> type.
    /// </summary>
    /// <param name="handler">The single handler that serves requests.</param>
    /// <param name="timeoutPolicy">The timeout policy applied to each request.</param>
    /// <param name="envelopeFactory">The envelope factory used to create request envelopes.</param>
    /// <param name="contextFactory">The context factory used to create handler contexts.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any constructor dependency is <see langword="null"/>.
    /// </exception>
    public InMemoryRequestClient(
        IRequestHandler<TRequest, TReply> handler,
        IRequestTimeoutPolicy timeoutPolicy,
        IMessageEnvelopeFactory envelopeFactory,
        IMessageContextFactory contextFactory)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _timeoutPolicy = timeoutPolicy ?? throw new ArgumentNullException(nameof(timeoutPolicy));
        _envelopeFactory = envelopeFactory ?? throw new ArgumentNullException(nameof(envelopeFactory));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    /// <inheritdoc/>
    public async Task<Reply<TReply>> SendAsync(TRequest request, CancellationToken cancellationToken = default)
    {
        var timeout = _timeoutPolicy.GetTimeout();
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            return new Reply<TReply>(ReplyStatus.Failure, value: default, error: "Timeout policy returned an invalid timeout.");
        }

        using var timeoutCts = timeout == Timeout.InfiniteTimeSpan ? null : new CancellationTokenSource(timeout);
        using var linkedCts = timeoutCts is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var operationToken = linkedCts?.Token ?? cancellationToken;

        var envelope = _envelopeFactory.Create(request);
        var context = _contextFactory.Create(envelope);

        try
        {
            var value = await _handler.HandleAsync(request, context, operationToken).ConfigureAwait(false);
            return new Reply<TReply>(ReplyStatus.Success, value, error: null);
        }
        catch (ArgumentException ex)
        {
            return new Reply<TReply>(ReplyStatus.ValidationError, value: default, error: ex.Message);
        }
        catch (OperationCanceledException ex) when (timeoutCts is not null && timeoutCts.IsCancellationRequested)
        {
            return new Reply<TReply>(ReplyStatus.Timeout, value: default, error: ex.Message);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            return new Reply<TReply>(ReplyStatus.Timeout, value: default, error: ex.Message);
        }
        catch (Exception ex)
        {
            return new Reply<TReply>(ReplyStatus.Failure, value: default, error: ex.Message);
        }
    }
}
