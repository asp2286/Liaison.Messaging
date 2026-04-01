namespace Liaison.Messaging.Hosting;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liaison.Messaging.AzureServiceBus;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Hosted service wrapper that manages the lifecycle of an
/// <see cref="AzureServiceBusRequestProcessor{TRequest, TReply}"/> within a .NET Generic Host.
/// </summary>
/// <typeparam name="TRequest">Request payload type.</typeparam>
/// <typeparam name="TReply">Reply payload type.</typeparam>
public sealed class AzureServiceBusRequestProcessorService<TRequest, TReply> : IHostedService, IAsyncDisposable
{
    private readonly AzureServiceBusRequestProcessor<TRequest, TReply> _processor;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureServiceBusRequestProcessorService{TRequest, TReply}"/> class.
    /// </summary>
    /// <param name="processor">The request processor whose lifecycle is managed by this hosted service.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="processor"/> is <see langword="null"/>.</exception>
    public AzureServiceBusRequestProcessorService(AzureServiceBusRequestProcessor<TRequest, TReply> processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        _processor = processor;
    }

    /// <summary>
    /// Starts the underlying Azure Service Bus request processor.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token provided by the host.</param>
    /// <returns>A task that completes when the processor has started.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _processor.StartAsync(cancellationToken);
    }

    /// <summary>
    /// Stops and disposes the underlying Azure Service Bus request processor.
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
            // Expected during host shutdown — swallow.
        }
    }

    /// <summary>
    /// Disposes the underlying Azure Service Bus request processor.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous dispose operation.</returns>
    public ValueTask DisposeAsync()
    {
        return _processor.DisposeAsync();
    }
}
