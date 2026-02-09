namespace Liaison.Messaging.AzureServiceBus;

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Liaison.Messaging;

/// <summary>
/// Represents an active Azure Service Bus subscription processor.
/// </summary>
/// <typeparam name="T">Message payload type.</typeparam>
public sealed class AzureServiceBusSubscription<T> : IMessageSubscription
{
    private readonly IMessageSerializer _serializer;
    private readonly IMessageContextFactory _contextFactory;
    private readonly IMessageHandler<T> _handler;
    private readonly ServiceBusProcessor _processor;
    private int _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureServiceBusSubscription{T}"/> type.
    /// </summary>
    /// <param name="client">Azure Service Bus client.</param>
    /// <param name="serializer">Serializer used to deserialize inbound payloads.</param>
    /// <param name="contextFactory">Factory used to create message contexts.</param>
    /// <param name="entityOptions">Queue or topic subscription settings.</param>
    /// <param name="handler">Message handler invoked for each received message.</param>
    /// <exception cref="ArgumentNullException">Thrown when required dependencies are <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when required entity settings are invalid.</exception>
    public AzureServiceBusSubscription(
        ServiceBusClient client,
        IMessageSerializer serializer,
        IMessageContextFactory contextFactory,
        AzureServiceBusEntityOptions entityOptions,
        IMessageHandler<T> handler)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));

        _processor = CreateProcessor(client ?? throw new ArgumentNullException(nameof(client)), entityOptions);
        _processor.ProcessMessageAsync += OnProcessMessageAsync;
        _processor.ProcessErrorAsync += OnProcessErrorAsync;
        _processor.StartProcessingAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _processor.ProcessMessageAsync -= OnProcessMessageAsync;
        _processor.ProcessErrorAsync -= OnProcessErrorAsync;

        await _processor.StopProcessingAsync().ConfigureAwait(false);
        await _processor.DisposeAsync().ConfigureAwait(false);
    }

    private static ServiceBusProcessor CreateProcessor(ServiceBusClient client, AzureServiceBusEntityOptions entityOptions)
    {
        if (entityOptions is null)
        {
            throw new ArgumentNullException(nameof(entityOptions));
        }

        if (string.IsNullOrWhiteSpace(entityOptions.EntityName))
        {
            throw new ArgumentException("Entity name must be provided.", nameof(entityOptions));
        }

        var processorOptions = new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
        };

        return entityOptions.Kind switch
        {
            AzureServiceBusEntityKind.Queue => client.CreateProcessor(entityOptions.EntityName, processorOptions),
            AzureServiceBusEntityKind.Topic when !string.IsNullOrWhiteSpace(entityOptions.SubscriptionName) =>
                client.CreateProcessor(entityOptions.EntityName, entityOptions.SubscriptionName, processorOptions),
            AzureServiceBusEntityKind.Topic => throw new ArgumentException(
                "Subscription name is required when receiving from a topic.",
                nameof(entityOptions)),
            _ => throw new ArgumentOutOfRangeException(nameof(entityOptions), "Unknown entity kind."),
        };
    }

    private async Task OnProcessMessageAsync(ProcessMessageEventArgs args)
    {
        try
        {
            var envelope = AzureServiceBusEnvelopeMapper.FromServiceBusReceivedMessage(args.Message);
            var message = _serializer.Deserialize<T>(envelope.Body);
            var context = _contextFactory.Create(envelope);

            await _handler.HandleAsync(message, context, args.CancellationToken).ConfigureAwait(false);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            try
            {
                await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (args.CancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private static Task OnProcessErrorAsync(ProcessErrorEventArgs args)
    {
        return Task.CompletedTask;
    }
}
