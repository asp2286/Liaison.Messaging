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
    private readonly ILargePayloadPolicy? _largePayloadPolicy;
    private readonly IPayloadStore? _payloadStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryRequestClient{TRequest, TReply}"/> type.
    /// </summary>
    /// <param name="handler">The single handler that serves requests.</param>
    /// <param name="timeout">The optional timeout for each request.</param>
    /// <param name="largePayloadPolicy">Optional large payload policy.</param>
    /// <param name="payloadStore">Optional payload store used when a large payload policy is supplied.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="handler"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeout"/> is negative.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when exactly one of <paramref name="largePayloadPolicy"/> or <paramref name="payloadStore"/> is provided.
    /// </exception>
    public InMemoryRequestClient(
        IRequestHandler<TRequest, TReply> handler,
        TimeSpan? timeout = null,
        ILargePayloadPolicy? largePayloadPolicy = null,
        IPayloadStore? payloadStore = null)
        : this(
            handler,
            timeoutPolicy: timeout.HasValue
                ? new FixedRequestTimeoutPolicy(timeout.Value)
                : new FixedRequestTimeoutPolicy(Timeout.InfiniteTimeSpan),
            largePayloadPolicy,
            payloadStore)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryRequestClient{TRequest, TReply}"/> type.
    /// </summary>
    /// <param name="handler">The single handler that serves requests.</param>
    /// <param name="timeoutPolicy">The timeout policy applied to each request.</param>
    /// <param name="largePayloadPolicy">Optional large payload policy.</param>
    /// <param name="payloadStore">Optional payload store used when a large payload policy is supplied.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="handler"/> or <paramref name="timeoutPolicy"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when exactly one of <paramref name="largePayloadPolicy"/> or <paramref name="payloadStore"/> is provided.
    /// </exception>
    public InMemoryRequestClient(
        IRequestHandler<TRequest, TReply> handler,
        IRequestTimeoutPolicy timeoutPolicy,
        ILargePayloadPolicy? largePayloadPolicy = null,
        IPayloadStore? payloadStore = null)
        : this(
            handler,
            timeoutPolicy,
            new MessageEnvelopeFactory(new SystemTextJsonMessageSerializer(), new GuidMessageIdGenerator()),
            new MessageContextFactory(),
            largePayloadPolicy,
            payloadStore)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryRequestClient{TRequest, TReply}"/> type.
    /// </summary>
    /// <param name="handler">The single handler that serves requests.</param>
    /// <param name="timeoutPolicy">The timeout policy applied to each request.</param>
    /// <param name="envelopeFactory">The envelope factory used to create request envelopes.</param>
    /// <param name="contextFactory">The context factory used to create handler contexts.</param>
    /// <param name="largePayloadPolicy">Optional large payload policy.</param>
    /// <param name="payloadStore">Optional payload store used when a large payload policy is supplied.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any constructor dependency is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when exactly one of <paramref name="largePayloadPolicy"/> or <paramref name="payloadStore"/> is provided.
    /// </exception>
    public InMemoryRequestClient(
        IRequestHandler<TRequest, TReply> handler,
        IRequestTimeoutPolicy timeoutPolicy,
        IMessageEnvelopeFactory envelopeFactory,
        IMessageContextFactory contextFactory,
        ILargePayloadPolicy? largePayloadPolicy = null,
        IPayloadStore? payloadStore = null)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _timeoutPolicy = timeoutPolicy ?? throw new ArgumentNullException(nameof(timeoutPolicy));
        _envelopeFactory = envelopeFactory ?? throw new ArgumentNullException(nameof(envelopeFactory));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        if ((largePayloadPolicy is null) != (payloadStore is null))
        {
            throw new ArgumentException(
                "Large payload policy and payload store must both be provided or both be null.");
        }

        _largePayloadPolicy = largePayloadPolicy;
        _payloadStore = payloadStore;
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
        var requestEnvelope = await PrepareRequestEnvelopeAsync(envelope, operationToken).ConfigureAwait(false);
        var context = _contextFactory.Create(requestEnvelope);

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

    private async Task<MessageEnvelope> PrepareRequestEnvelopeAsync(
        MessageEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (_largePayloadPolicy is null || _payloadStore is null)
        {
            return envelope;
        }

        var outboundEnvelope = await _largePayloadPolicy
            .PrepareOutboundAsync(envelope, _payloadStore, expiresAtUtc: null, cancellationToken)
            .ConfigureAwait(false);
        return await _largePayloadPolicy
            .ResolveInboundAsync(outboundEnvelope, _payloadStore, cancellationToken)
            .ConfigureAwait(false);
    }
}
