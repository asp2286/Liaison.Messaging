namespace Liaison.Messaging.AwsSqs;

using System;
using Amazon.SQS;
using Liaison.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

/// <summary>
/// Dependency injection extensions for Amazon SQS messaging components.
/// </summary>
public static class SqsServiceCollectionExtensions
{
    /// <summary>
    /// Registers a preconfigured singleton <see cref="IAmazonSQS"/> client.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="client">Preconfigured Amazon SQS client.</param>
    /// <returns>The same service collection instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="client"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddSqsClient(
        this IServiceCollection services,
        IAmazonSQS client)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(client);

        services.TryAddSingleton(client);
        return services;
    }

    /// <summary>
    /// Registers a singleton <see cref="IAmazonSQS"/> client factory.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="factory">Factory that creates the preconfigured Amazon SQS client.</param>
    /// <returns>The same service collection instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="factory"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddSqsClient(
        this IServiceCollection services,
        Func<IServiceProvider, IAmazonSQS> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);

        services.TryAddSingleton(factory);
        return services;
    }

    /// <summary>
    /// Registers <see cref="SqsPublisher{T}"/> as the singleton <see cref="IMessagePublisher{T}"/>.
    /// </summary>
    /// <typeparam name="T">Message payload type.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Callback that configures the target queue options.</param>
    /// <returns>The same service collection instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when configured queue options are invalid.</exception>
    public static IServiceCollection AddSqsPublisher<T>(
        this IServiceCollection services,
        Action<SqsQueueOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SqsQueueOptions();
        configure(options);
        options.Validate();

        services.AddSingleton<IMessagePublisher<T>>(sp =>
        {
            var client = sp.GetRequiredService<IAmazonSQS>();
            var envelopeFactory = sp.GetRequiredService<IMessageEnvelopeFactory>();
            var largePayloadPolicy = sp.GetService<ILargePayloadPolicy>();
            var payloadStore = sp.GetService<IPayloadStore>();
            return new SqsPublisher<T>(
                client, envelopeFactory, options, largePayloadPolicy, payloadStore);
        });

        return services;
    }

    /// <summary>
    /// Registers a singleton <see cref="SqsSubscription{T}"/> and its message handler.
    /// </summary>
    /// <typeparam name="T">Message payload type.</typeparam>
    /// <typeparam name="THandler">Concrete handler type that implements <see cref="IMessageHandler{T}"/>.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Callback that configures the queue options.</param>
    /// <returns>The same service collection instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when configured queue options are invalid.</exception>
    public static IServiceCollection AddSqsSubscription<T, THandler>(
        this IServiceCollection services,
        Action<SqsQueueOptions> configure)
        where THandler : class, IMessageHandler<T>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SqsQueueOptions();
        configure(options);
        options.Validate();

        services.TryAddSingleton<THandler>();
        services.AddSingleton(sp =>
        {
            var client = sp.GetRequiredService<IAmazonSQS>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var contextFactory = sp.GetRequiredService<IMessageContextFactory>();
            var handler = sp.GetRequiredService<THandler>();
            var logger = sp.GetService<ILogger<SqsSubscription<T>>>();
            var largePayloadPolicy = sp.GetService<ILargePayloadPolicy>();
            var payloadStore = sp.GetService<IPayloadStore>();
            return new SqsSubscription<T>(
                client, serializer, contextFactory, options, handler, logger,
                largePayloadPolicy, payloadStore);
        });

        return services;
    }

    /// <summary>
    /// Registers <see cref="SqsRequestClient{TRequest, TReply}"/> as the singleton
    /// <see cref="IRequestClient{TRequest, TReply}"/>.
    /// </summary>
    /// <typeparam name="TRequest">Request payload type.</typeparam>
    /// <typeparam name="TReply">Reply payload type.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Callback that configures the request/reply options.</param>
    /// <returns>The same service collection instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when configured request/reply options are invalid.</exception>
    public static IServiceCollection AddSqsRequestClient<TRequest, TReply>(
        this IServiceCollection services,
        Action<SqsRequestReplyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SqsRequestReplyOptions();
        configure(options);
        options.Validate();

        services.AddSingleton<IRequestClient<TRequest, TReply>>(sp =>
        {
            var client = sp.GetRequiredService<IAmazonSQS>();
            var envelopeFactory = sp.GetRequiredService<IMessageEnvelopeFactory>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var timeoutPolicy = sp.GetService<IRequestTimeoutPolicy>();
            var logger = sp.GetService<ILogger<SqsRequestClient<TRequest, TReply>>>();
            var largePayloadPolicy = sp.GetService<ILargePayloadPolicy>();
            var payloadStore = sp.GetService<IPayloadStore>();
            return new SqsRequestClient<TRequest, TReply>(
                client, envelopeFactory, serializer, timeoutPolicy, options, logger,
                largePayloadPolicy, payloadStore);
        });

        return services;
    }

    /// <summary>
    /// Registers a singleton <see cref="SqsRequestProcessor{TRequest, TReply}"/> and its request handler.
    /// </summary>
    /// <typeparam name="TRequest">Request payload type.</typeparam>
    /// <typeparam name="TReply">Reply payload type.</typeparam>
    /// <typeparam name="THandler">Concrete handler type that implements <see cref="IRequestHandler{TRequest, TReply}"/>.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Callback that configures the request/reply options.</param>
    /// <returns>The same service collection instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when configured request/reply options are invalid.</exception>
    public static IServiceCollection AddSqsRequestProcessor<TRequest, TReply, THandler>(
        this IServiceCollection services,
        Action<SqsRequestReplyOptions> configure)
        where THandler : class, IRequestHandler<TRequest, TReply>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SqsRequestReplyOptions();
        configure(options);
        options.Validate();

        services.TryAddSingleton<THandler>();
        services.AddSingleton(sp =>
        {
            var client = sp.GetRequiredService<IAmazonSQS>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var contextFactory = sp.GetRequiredService<IMessageContextFactory>();
            var handler = sp.GetRequiredService<THandler>();
            var logger = sp.GetService<ILogger<SqsRequestProcessor<TRequest, TReply>>>();
            var largePayloadPolicy = sp.GetService<ILargePayloadPolicy>();
            var payloadStore = sp.GetService<IPayloadStore>();
            return new SqsRequestProcessor<TRequest, TReply>(
                client, serializer, contextFactory, options, handler, logger,
                largePayloadPolicy, payloadStore);
        });

        return services;
    }
}
