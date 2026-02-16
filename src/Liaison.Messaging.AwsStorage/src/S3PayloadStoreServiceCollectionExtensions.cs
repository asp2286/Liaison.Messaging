namespace Liaison.Messaging.AwsStorage;

using System;
using Liaison.Messaging;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Dependency injection extensions for <see cref="AwsS3PayloadStore"/>.
/// </summary>
public static class S3PayloadStoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AwsS3PayloadStore"/> as the singleton <see cref="IPayloadStore"/>.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Options configuration callback.</param>
    /// <returns>The same service collection instance.</returns>
    public static IServiceCollection AddS3PayloadStore(
        this IServiceCollection services,
        Action<S3PayloadStoreOptions> configure)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        services.AddOptions<S3PayloadStoreOptions>().Configure(configure);
        services.AddSingleton<IPayloadStore, AwsS3PayloadStore>();
        return services;
    }
}
