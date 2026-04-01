namespace Liaison.Messaging.Hosting;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liaison.Messaging.AzureServiceBus;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Hosted service wrapper that manages the lifecycle of an
/// <see cref="AzureServiceBusSubscription{T}"/> within a .NET Generic Host.
/// </summary>
/// <typeparam name="T">Message payload type.</typeparam>
public sealed class AzureServiceBusSubscriptionService<T> : IHostedService, IAsyncDisposable
{
    private readonly AzureServiceBusSubscription<T> _subscription;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureServiceBusSubscriptionService{T}"/> class.
    /// </summary>
    /// <param name="subscription">The subscription whose lifecycle is managed by this hosted service.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="subscription"/> is <see langword="null"/>.</exception>
    public AzureServiceBusSubscriptionService(AzureServiceBusSubscription<T> subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        _subscription = subscription;
    }

    /// <summary>
    /// Starts the underlying Azure Service Bus subscription processor.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token provided by the host.</param>
    /// <returns>A task that completes when the processor has started.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _subscription.StartAsync(cancellationToken);
    }

    /// <summary>
    /// Stops and disposes the underlying Azure Service Bus subscription processor.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token provided by the host.</param>
    /// <returns>A task that completes when the processor has stopped.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _subscription.DisposeAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during host shutdown — swallow.
        }
    }

    /// <summary>
    /// Disposes the underlying Azure Service Bus subscription processor.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous dispose operation.</returns>
    public ValueTask DisposeAsync()
    {
        return _subscription.DisposeAsync();
    }
}
