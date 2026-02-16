namespace Liaison.Messaging.AzureStorage;

using System;
using Liaison.Messaging;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Dependency injection extensions for <see cref="AzureBlobPayloadStore"/>.
/// </summary>
public static class AzureBlobPayloadStoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AzureBlobPayloadStore"/> as the singleton <see cref="IPayloadStore"/>.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Options configuration callback.</param>
    /// <returns>The same service collection instance.</returns>
    public static IServiceCollection AddAzureBlobPayloadStore(
        this IServiceCollection services,
        Action<AzureBlobPayloadStoreOptions> configure)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        services.AddOptions<AzureBlobPayloadStoreOptions>().Configure(configure);
        services.AddSingleton<IPayloadStore, AzureBlobPayloadStore>();
        return services;
    }
}
