namespace Liaison.Messaging;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Registers the default core messaging services used by Liaison.Messaging.
/// </summary>
public static class LiaisonMessagingDefaultsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the default serializer, ID generator, envelope factory, and context factory.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection instance.</returns>
    public static IServiceCollection AddLiaisonMessagingDefaults(
        this IServiceCollection services)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(services);
#else
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }
#endif

        services.TryAddSingleton<IMessageSerializer, SystemTextJsonMessageSerializer>();
        services.TryAddSingleton<IMessageIdGenerator, GuidMessageIdGenerator>();
        services.TryAddSingleton<IMessageEnvelopeFactory, MessageEnvelopeFactory>();
        services.TryAddSingleton<IMessageContextFactory, MessageContextFactory>();
        return services;
    }
}
