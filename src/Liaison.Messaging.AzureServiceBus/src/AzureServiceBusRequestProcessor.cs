namespace Liaison.Messaging.AzureServiceBus;

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Liaison.Messaging;

/// <summary>
/// Processes request messages from Azure Service Bus and sends correlated replies.
/// </summary>
/// <typeparam name="TRequest">Request payload type.</typeparam>
/// <typeparam name="TReply">Reply payload type.</typeparam>
public sealed class AzureServiceBusRequestProcessor<TRequest, TReply> : IAsyncDisposable
{
    private readonly IMessageSerializer _serializer;
    private readonly IMessageContextFactory _contextFactory;
    private readonly IRequestHandler<TRequest, TReply> _handler;
    private readonly ServiceBusProcessor _processor;
    private readonly ServiceBusSender _replySender;
    private readonly IMessageIdGenerator _messageIdGenerator;
    private int _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureServiceBusRequestProcessor{TRequest, TReply}"/> type.
    /// </summary>
    /// <param name="client">Azure Service Bus client.</param>
    /// <param name="serializer">Serializer used for request and reply payloads.</param>
    /// <param name="contextFactory">Factory used to create request contexts.</param>
    /// <param name="options">Request/reply queue settings.</param>
    /// <param name="handler">Request handler.</param>
    /// <exception cref="ArgumentNullException">Thrown when required dependencies are <see langword="null"/>.</exception>
    public AzureServiceBusRequestProcessor(
        ServiceBusClient client,
        IMessageSerializer serializer,
        IMessageContextFactory contextFactory,
        AzureServiceBusRequestReplyOptions options,
        IRequestHandler<TRequest, TReply> handler)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _messageIdGenerator = new GuidMessageIdGenerator();

        var busClient = client ?? throw new ArgumentNullException(nameof(client));
        _replySender = busClient.CreateSender(options.ReplyQueueName);
        _processor = busClient.CreateProcessor(options.RequestQueueName, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
        });

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
        await _replySender.DisposeAsync().ConfigureAwait(false);
    }

    private async Task OnProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var requestEnvelope = AzureServiceBusEnvelopeMapper.FromServiceBusReceivedMessage(args.Message);
        var correlationId = string.IsNullOrWhiteSpace(requestEnvelope.MessageId)
            ? requestEnvelope.CorrelationId
            : requestEnvelope.MessageId;

        try
        {
            var request = _serializer.Deserialize<TRequest>(requestEnvelope.Body);
            var context = _contextFactory.Create(requestEnvelope);
            var replyPayload = await _handler.HandleAsync(request, context, args.CancellationToken).ConfigureAwait(false);

            await SendReplyAsync(
                    replyPayload,
                    correlationId,
                    status: null,
                    error: null,
                    cancellationToken: args.CancellationToken)
                .ConfigureAwait(false);

            await args.CompleteMessageAsync(args.Message, args.CancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            await SendReplyAsync(
                    value: default,
                    correlationId,
                    ReplyStatus.ValidationError,
                    ex.Message,
                    args.CancellationToken)
                .ConfigureAwait(false);

            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await SendReplyAsync(
                    value: default,
                    correlationId,
                    ReplyStatus.Failure,
                    "Request processing failed.",
                    args.CancellationToken)
                .ConfigureAwait(false);

            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken).ConfigureAwait(false);
        }
    }

    private static Task OnProcessErrorAsync(ProcessErrorEventArgs args)
    {
        return Task.CompletedTask;
    }

    private async Task SendReplyAsync(
        TReply? value,
        string? correlationId,
        ReplyStatus? status,
        string? error,
        CancellationToken cancellationToken)
    {
        var payload = value is null ? Array.Empty<byte>() : _serializer.Serialize(value);
        var replyMessage = new ServiceBusMessage(BinaryData.FromBytes(payload))
        {
            MessageId = _messageIdGenerator.NewId(),
            CorrelationId = correlationId,
        };

        if (status.HasValue)
        {
            replyMessage.ApplicationProperties[AzureServiceBusReplyHeaders.Status] = status.Value.ToString();
            if (!string.IsNullOrWhiteSpace(error))
            {
                replyMessage.ApplicationProperties[AzureServiceBusReplyHeaders.Error] = error;
            }
        }

        await _replySender.SendMessageAsync(replyMessage, cancellationToken).ConfigureAwait(false);
    }
}
