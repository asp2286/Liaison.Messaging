namespace Liaison.Messaging.AzureServiceBus;

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Liaison.Messaging;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger? _logger;
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
    /// <param name="logger">Optional logger for diagnostics. When <see langword="null"/>, broker errors are silently ignored.</param>
    /// <exception cref="ArgumentNullException">Thrown when required dependencies are <see langword="null"/>.</exception>
    public AzureServiceBusRequestProcessor(
        ServiceBusClient client,
        IMessageSerializer serializer,
        IMessageContextFactory contextFactory,
        AzureServiceBusRequestReplyOptions options,
        IRequestHandler<TRequest, TReply> handler,
        ILogger<AzureServiceBusRequestProcessor<TRequest, TReply>>? logger = null)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _messageIdGenerator = new GuidMessageIdGenerator();
        _logger = logger;

        var busClient = client ?? throw new ArgumentNullException(nameof(client));
        _replySender = busClient.CreateSender(options.ReplyQueueName);
        _processor = busClient.CreateProcessor(options.RequestQueueName, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
        });

        _processor.ProcessMessageAsync += OnProcessMessageAsync;
        _processor.ProcessErrorAsync += OnProcessErrorAsync;
    }

    /// <summary>
    /// Starts processing request messages from the configured queue.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the processor has started.</returns>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return _processor.StartProcessingAsync(cancellationToken);
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

        // Step 1: Process the request and build the reply.
        TReply? replyPayload = default;
        ReplyStatus? replyStatus = null;
        string? replyError = null;

        try
        {
            var request = _serializer.Deserialize<TRequest>(requestEnvelope.Body);
            var context = _contextFactory.Create(requestEnvelope);
            replyPayload = await _handler.HandleAsync(request, context, args.CancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            replyStatus = ReplyStatus.ValidationError;
            replyError = ex.Message;
        }
        catch (Exception)
        {
            replyStatus = ReplyStatus.Failure;
            replyError = "Request processing failed.";
        }

        // Step 2: Try to send the reply.
        try
        {
            await SendReplyAsync(replyPayload, correlationId, replyStatus, replyError, args.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Reply could not be sent — abandon the request so it can be retried.
            _logger?.LogError(ex, "Failed to send reply for request. CorrelationId={CorrelationId}", correlationId);
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken).ConfigureAwait(false);
            return;
        }

        // Step 3: Reply was sent successfully — complete the request message.
        await args.CompleteMessageAsync(args.Message, args.CancellationToken).ConfigureAwait(false);
    }

    private Task OnProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger?.LogError(
            args.Exception,
            "Azure Service Bus processing error. Source={ErrorSource} Entity={EntityPath} Namespace={Namespace}",
            args.ErrorSource,
            args.EntityPath,
            args.FullyQualifiedNamespace);
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
