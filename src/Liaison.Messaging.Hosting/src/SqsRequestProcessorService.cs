namespace Liaison.Messaging.Hosting;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liaison.Messaging.AwsSqs;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Hosted service wrapper that manages the lifecycle of an
/// <see cref="SqsRequestProcessor{TRequest, TReply}"/> within a .NET Generic Host.
/// </summary>
/// <typeparam name="TRequest">Request payload type.</typeparam>
/// <typeparam name="TReply">Reply payload type.</typeparam>
public sealed class SqsRequestProcessorService<TRequest, TReply> : IHostedService, IAsyncDisposable
{
    private readonly SqsRequestProcessor<TRequest, TReply> _processor;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqsRequestProcessorService{TRequest, TReply}"/> class.
    /// </summary>
    /// <param name="processor">The request processor whose lifecycle is managed by this hosted service.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="processor"/> is <see langword="null"/>.</exception>
    public SqsRequestProcessorService(SqsRequestProcessor<TRequest, TReply> processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        _processor = processor;
    }

    /// <summary>
    /// Starts the underlying Amazon SQS request processor.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token provided by the host.</param>
    /// <returns>A task that completes when the receive loop has been scheduled.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _processor.StartAsync(cancellationToken);
    }

    /// <summary>
    /// Stops and disposes the underlying Amazon SQS request processor.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token provided by the host.</param>
    /// <returns>A task that completes when the processor has stopped.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _processor.DisposeAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during host shutdown - swallow.
        }
    }

    /// <summary>
    /// Disposes the underlying Amazon SQS request processor.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous dispose operation.</returns>
    public ValueTask DisposeAsync()
    {
        return _processor.DisposeAsync();
    }
}
