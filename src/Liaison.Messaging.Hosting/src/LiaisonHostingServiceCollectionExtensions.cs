namespace Liaison.Messaging.Hosting;

using System;
using Liaison.Messaging.AzureServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods for registering Liaison.Messaging hosted services with the
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class LiaisonHostingServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="AzureServiceBusSubscriptionService{T}"/> as a hosted service
    /// so that the .NET Generic Host manages the subscription lifecycle.
    /// </summary>
    /// <typeparam name="T">Message payload type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <remarks>
    /// Requires that <see cref="AzureServiceBusSubscription{T}"/> is already registered
    /// (e.g. via <c>AddAzureServiceBusSubscription</c>).
    /// </remarks>
    public static IServiceCollection AddAzureServiceBusSubscriptionService<T>(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<AzureServiceBusSubscriptionService<T>>(sp =>
            new AzureServiceBusSubscriptionService<T>(
                sp.GetRequiredService<AzureServiceBusSubscription<T>>()));

        services.AddHostedService<AzureServiceBusSubscriptionService<T>>(
            sp => sp.GetRequiredService<AzureServiceBusSubscriptionService<T>>());

        return services;
    }

    /// <summary>
    /// Registers an <see cref="AzureServiceBusRequestProcessorService{TRequest, TReply}"/> as a
    /// hosted service so that the .NET Generic Host manages the request processor lifecycle.
    /// </summary>
    /// <typeparam name="TRequest">Request payload type.</typeparam>
    /// <typeparam name="TReply">Reply payload type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <remarks>
    /// Requires that <see cref="AzureServiceBusRequestProcessor{TRequest, TReply}"/> is already
    /// registered (e.g. via <c>AddAzureServiceBusRequestProcessor</c>).
    /// </remarks>
    public static IServiceCollection AddAzureServiceBusRequestProcessorService<TRequest, TReply>(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<AzureServiceBusRequestProcessorService<TRequest, TReply>>(sp =>
            new AzureServiceBusRequestProcessorService<TRequest, TReply>(
                sp.GetRequiredService<AzureServiceBusRequestProcessor<TRequest, TReply>>()));

        services.AddHostedService<AzureServiceBusRequestProcessorService<TRequest, TReply>>(
            sp => sp.GetRequiredService<AzureServiceBusRequestProcessorService<TRequest, TReply>>());

        return services;
    }
}
