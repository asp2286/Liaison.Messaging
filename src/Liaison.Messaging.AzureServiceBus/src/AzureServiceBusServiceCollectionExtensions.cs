namespace Liaison.Messaging.AzureServiceBus;

using System;
using System.Threading;
using Azure.Messaging.ServiceBus;
using Liaison.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

/// <summary>
/// Dependency injection extensions for Azure Service Bus messaging components.
/// </summary>
public static class AzureServiceBusServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="ServiceBusClient"/> created from the provided connection string.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="connectionString">Azure Service Bus connection string.</param>
    /// <returns>The same service collection instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is empty or whitespace.</exception>
    public static IServiceCollection AddAzureServiceBusClient(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must be provided.", nameof(connectionString));
        }

        var options = new AzureServiceBusConnectionOptions(connectionString);
        services.TryAddSingleton(options);
        services.TryAddSingleton(sp => new ServiceBusClient(sp.GetRequiredService<AzureServiceBusConnectionOptions>().ConnectionString));
        return services;
    }

    /// <summary>
    /// Registers <see cref="AzureServiceBusPublisher{T}"/> as the singleton
    /// <see cref="IMessagePublisher{T}"/> for the specified message type.
    /// </summary>
    /// <typeparam name="T">Message payload type.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Callback that configures the target entity options.</param>
    /// <returns>The same service collection instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when configured entity options are invalid.</exception>
    public static IServiceCollection AddAzureServiceBusPublisher<T>(
        this IServiceCollection services,
        Action<AzureServiceBusEntityOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var entityOptions = new AzureServiceBusEntityOptions();
        configure(entityOptions);
        ValidateEntityOptions(entityOptions);

        services.AddSingleton<IMessagePublisher<T>>(sp =>
        {
            var client = sp.GetRequiredService<ServiceBusClient>();
            var envelopeFactory = sp.GetRequiredService<IMessageEnvelopeFactory>();
            var largePayloadPolicy = sp.GetService<ILargePayloadPolicy>();
            var payloadStore = sp.GetService<IPayloadStore>();
            return new AzureServiceBusPublisher<T>(
                client, envelopeFactory, entityOptions, largePayloadPolicy, payloadStore);
        });

        return services;
    }

    /// <summary>
    /// Registers <see cref="AzureServiceBusPublisher{T}"/> as the singleton
    /// <see cref="IMessagePublisher{T}"/> for the specified message type, using an explicit entity router.
    /// </summary>
    /// <typeparam name="T">Message payload type.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Callback that configures the target entity options.</param>
    /// <param name="router">Entity router that resolves the target entity for each envelope.</param>
    /// <returns>The same service collection instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/>, <paramref name="configure"/>, or <paramref name="router"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when configured entity options are invalid.</exception>
    public static IServiceCollection AddAzureServiceBusPublisher<T>(
        this IServiceCollection services,
        Action<AzureServiceBusEntityOptions> configure,
        IAzureServiceBusEntityRouter router)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(router);

        var entityOptions = new AzureServiceBusEntityOptions();
        configure(entityOptions);
        ValidateEntityOptions(entityOptions);

        services.AddSingleton<IMessagePublisher<T>>(sp =>
        {
            var client = sp.GetRequiredService<ServiceBusClient>();
            var envelopeFactory = sp.GetRequiredService<IMessageEnvelopeFactory>();
            var largePayloadPolicy = sp.GetService<ILargePayloadPolicy>();
            var payloadStore = sp.GetService<IPayloadStore>();
            return new AzureServiceBusPublisher<T>(
                client, envelopeFactory, entityOptions, router, largePayloadPolicy, payloadStore);
        });

        return services;
    }

    /// <summary>
    /// Registers a singleton <see cref="AzureServiceBusSubscription{T}"/> and its message handler.
    /// The handler type <typeparamref name="THandler"/> is registered as a singleton if not already present.
    /// The subscription is registered as its concrete type; <see cref="IMessageSubscription"/> is
    /// not registered — the caller manages lifecycle explicitly.
    /// </summary>
    /// <typeparam name="T">Message payload type.</typeparam>
    /// <typeparam name="THandler">Concrete handler type that implements <see cref="IMessageHandler{T}"/>.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Callback that configures the entity options.</param>
    /// <returns>The same service collection instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when configured entity options are invalid.</exception>
    public static IServiceCollection AddAzureServiceBusSubscription<T, THandler>(
        this IServiceCollection services,
        Action<AzureServiceBusEntityOptions> configure)
        where THandler : class, IMessageHandler<T>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var entityOptions = new AzureServiceBusEntityOptions();
        configure(entityOptions);
        ValidateEntityOptions(entityOptions);

        services.TryAddSingleton<THandler>();

        services.AddSingleton(sp =>
        {
            var client = sp.GetRequiredService<ServiceBusClient>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var contextFactory = sp.GetRequiredService<IMessageContextFactory>();
            var handler = sp.GetRequiredService<THandler>();
            var logger = sp.GetService<ILogger<AzureServiceBusSubscription<T>>>();
            var largePayloadPolicy = sp.GetService<ILargePayloadPolicy>();
            var payloadStore = sp.GetService<IPayloadStore>();
            return new AzureServiceBusSubscription<T>(
                client, serializer, contextFactory, entityOptions, handler, logger,
                largePayloadPolicy, payloadStore);
        });

        return services;
    }

    /// <summary>
    /// Registers <see cref="AzureServiceBusRequestClient{TRequest, TReply}"/> as the singleton
    /// <see cref="IRequestClient{TRequest, TReply}"/>. If <see cref="IRequestTimeoutPolicy"/> is
    /// registered in the container it is used; otherwise the default timeout from options applies.
    /// </summary>
    /// <typeparam name="TRequest">Request payload type.</typeparam>
    /// <typeparam name="TReply">Reply payload type.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Callback that configures the request/reply options.</param>
    /// <returns>The same service collection instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when configured request/reply options are invalid.</exception>
    public static IServiceCollection AddAzureServiceBusRequestClient<TRequest, TReply>(
        this IServiceCollection services,
        Action<AzureServiceBusRequestReplyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new AzureServiceBusRequestReplyOptions();
        configure(options);
        ValidateRequestReplyOptions(options);

        services.AddSingleton<IRequestClient<TRequest, TReply>>(sp =>
        {
            var client = sp.GetRequiredService<ServiceBusClient>();
            var envelopeFactory = sp.GetRequiredService<IMessageEnvelopeFactory>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var timeoutPolicy = sp.GetService<IRequestTimeoutPolicy>();
            var logger = sp.GetService<ILogger<AzureServiceBusRequestClient<TRequest, TReply>>>();
            var largePayloadPolicy = sp.GetService<ILargePayloadPolicy>();
            var payloadStore = sp.GetService<IPayloadStore>();
            return new AzureServiceBusRequestClient<TRequest, TReply>(
                client, envelopeFactory, serializer, timeoutPolicy, options, logger,
                largePayloadPolicy, payloadStore);
        });

        return services;
    }

    /// <summary>
    /// Registers a singleton <see cref="AzureServiceBusRequestProcessor{TRequest, TReply}"/> and
    /// its request handler. The handler type <typeparamref name="THandler"/> is registered as a
    /// singleton if not already present.
    /// </summary>
    /// <typeparam name="TRequest">Request payload type.</typeparam>
    /// <typeparam name="TReply">Reply payload type.</typeparam>
    /// <typeparam name="THandler">Concrete handler type that implements <see cref="IRequestHandler{TRequest, TReply}"/>.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Callback that configures the request/reply options.</param>
    /// <returns>The same service collection instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when configured request/reply options are invalid.</exception>
    public static IServiceCollection AddAzureServiceBusRequestProcessor<TRequest, TReply, THandler>(
        this IServiceCollection services,
        Action<AzureServiceBusRequestReplyOptions> configure)
        where THandler : class, IRequestHandler<TRequest, TReply>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new AzureServiceBusRequestReplyOptions();
        configure(options);
        ValidateRequestReplyOptions(options);

        services.TryAddSingleton<THandler>();

        services.AddSingleton(sp =>
        {
            var client = sp.GetRequiredService<ServiceBusClient>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var contextFactory = sp.GetRequiredService<IMessageContextFactory>();
            var handler = sp.GetRequiredService<THandler>();
            var logger = sp.GetService<ILogger<AzureServiceBusRequestProcessor<TRequest, TReply>>>();
            var largePayloadPolicy = sp.GetService<ILargePayloadPolicy>();
            var payloadStore = sp.GetService<IPayloadStore>();
            return new AzureServiceBusRequestProcessor<TRequest, TReply>(
                client, serializer, contextFactory, options, handler, logger,
                largePayloadPolicy, payloadStore);
        });

        return services;
    }

    private static void ValidateEntityOptions(AzureServiceBusEntityOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.EntityName))
        {
            throw new ArgumentException("Entity name must be provided.");
        }
    }

    private static void ValidateRequestReplyOptions(AzureServiceBusRequestReplyOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RequestQueueName))
        {
            throw new ArgumentException("Request queue name must be provided.");
        }

        if (string.IsNullOrWhiteSpace(options.ReplyQueueName))
        {
            throw new ArgumentException("Reply queue name must be provided.");
        }

        if (options.DefaultTimeout < TimeSpan.Zero && options.DefaultTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentException("Default timeout must be non-negative or infinite.");
        }
    }
}
