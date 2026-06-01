namespace Liaison.Messaging;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Dependency injection extensions for protobuf message serialization.
/// </summary>
public static class ProtobufSerializationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ProtobufMessageSerializer"/> as the singleton <see cref="IMessageSerializer"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection instance.</returns>
    /// <remarks>
    /// This method uses <see cref="ServiceCollectionDescriptorExtensions.TryAddSingleton{TService, TImplementation}(IServiceCollection)"/>.
    /// If this method and <c>AddLiaisonMessagingDefaults()</c> are both called, the first serializer registration wins.
    /// Call <c>AddProtobufMessageSerializer()</c> before <c>AddLiaisonMessagingDefaults()</c> to use protobuf, or register only one serializer.
    /// </remarks>
    public static IServiceCollection AddProtobufMessageSerializer(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IMessageSerializer, ProtobufMessageSerializer>();
        return services;
    }
}
