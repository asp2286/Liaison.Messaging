namespace Liaison.Messaging.Tests;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Liaison.Messaging;
using Liaison.Messaging.AwsSqs;
using Liaison.Messaging.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SqsMessage = Amazon.SQS.Model.Message;

/// <summary>
/// Unit tests for the Amazon SQS transport adapter.
/// </summary>
public sealed class SqsTransportTests
{
    private const string QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/test-queue";
    private const string RequestQueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/request-queue";
    private const string ReplyQueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/reply-queue";

    [Fact]
    public void EnvelopeMapper_RoundTripsEnvelopeMetadataHeadersAndBody()
    {
        var body = new byte[] { 0x00, 0xFF, 0x80, 0xC3, 0x28 };
        var envelope = new MessageEnvelope(
            "message-1",
            "correlation-1",
            DateTimeOffset.UtcNow,
            new ReadOnlyMemory<byte>(body),
            new Dictionary<string, string>
            {
                ["content-type"] = "application/octet-stream",
                ["custom"] = "value",
            });

        var sqsMessage = new SqsMessage
        {
            MessageId = "sqs-assigned-id",
            Body = SqsEnvelopeMapper.ToSqsMessageBody(envelope),
            MessageAttributes = SqsEnvelopeMapper.ToSqsMessageAttributes(envelope),
        };

        var mapped = SqsEnvelopeMapper.FromSqsMessage(sqsMessage);

        Assert.Equal(envelope.MessageId, mapped.MessageId);
        Assert.Equal(envelope.CorrelationId, mapped.CorrelationId);
        Assert.Equal(envelope.Body.ToArray(), mapped.Body.ToArray());
        Assert.Equal("application/octet-stream", mapped.Headers["content-type"]);
        Assert.Equal("value", mapped.Headers["custom"]);
        Assert.False(mapped.Headers.ContainsKey("liaison.message-id"));
        Assert.False(mapped.Headers.ContainsKey("liaison.correlation-id"));
    }

    [Fact]
    public void EnvelopeMapper_UsesSqsMessageIdWhenLiaisonMessageIdIsAbsent()
    {
        var envelope = new MessageEnvelope(
            "message-1",
            correlationId: null,
            DateTimeOffset.UtcNow,
            Encoding.UTF8.GetBytes("body"),
            new Dictionary<string, string>());
        var attributes = SqsEnvelopeMapper.ToSqsMessageAttributes(envelope);
        attributes.Remove("liaison.message-id");

        var mapped = SqsEnvelopeMapper.FromSqsMessage(new SqsMessage
        {
            MessageId = "sqs-assigned-id",
            Body = SqsEnvelopeMapper.ToSqsMessageBody(envelope),
            MessageAttributes = attributes,
        });

        Assert.Equal("sqs-assigned-id", mapped.MessageId);
    }

    [Fact]
    public void EnvelopeMapper_IgnoresNonStringMessageAttributes()
    {
        var envelope = new MessageEnvelope(
            "message-1",
            correlationId: null,
            DateTimeOffset.UtcNow,
            Encoding.UTF8.GetBytes("body"),
            new Dictionary<string, string>());
        var attributes = SqsEnvelopeMapper.ToSqsMessageAttributes(envelope);
        attributes["binary"] = new MessageAttributeValue
        {
            DataType = "Binary",
            BinaryValue = new MemoryStream(new byte[] { 1, 2, 3 }),
        };

        var mapped = SqsEnvelopeMapper.FromSqsMessage(new SqsMessage
        {
            MessageId = "sqs-assigned-id",
            Body = SqsEnvelopeMapper.ToSqsMessageBody(envelope),
            MessageAttributes = attributes,
        });

        Assert.False(mapped.Headers.ContainsKey("binary"));
    }

    [Fact]
    public async Task Publisher_WithPolicy_CallsPrepareOutboundBeforeSend()
    {
        var policy = new CapturingLargePayloadPolicy();
        var store = new NoOpPayloadStore();
        var client = new FakeSqsClient
        {
            OnSend = _ => Assert.Equal(1, policy.PrepareOutboundCallCount),
        };
        var publisher = new SqsPublisher<string>(
            client,
            new FixedEnvelopeFactory("message-1"),
            new SqsQueueOptions(QueueUrl),
            policy,
            store);

        await publisher.PublishAsync("hello");

        Assert.Equal(1, policy.PrepareOutboundCallCount);
        Assert.Equal(0, policy.ResolveInboundCallCount);
        Assert.Single(client.SendRequests);
    }

    [Fact]
    public async Task Publisher_WithoutPolicy_SendsOnce()
    {
        var client = new FakeSqsClient();
        var publisher = new SqsPublisher<string>(
            client,
            new FixedEnvelopeFactory("message-1"),
            new SqsQueueOptions(QueueUrl));

        await publisher.PublishAsync("hello");

        Assert.Single(client.SendRequests);
    }

    [Fact]
    public async Task Publisher_WithPolicyButNullStore_ThrowsInvalidOperationException()
    {
        var client = new FakeSqsClient();
        var publisher = new SqsPublisher<string>(
            client,
            new FixedEnvelopeFactory("message-1"),
            new SqsQueueOptions(QueueUrl),
            new CapturingLargePayloadPolicy(),
            payloadStore: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync("hello"));
        Assert.Contains("IPayloadStore", ex.Message, StringComparison.Ordinal);
        Assert.Empty(client.SendRequests);
    }

    [Fact]
    public async Task Publisher_Fifo_SetsMessageGroupAndDeduplicationId()
    {
        var client = new FakeSqsClient();
        var publisher = new SqsPublisher<string>(
            client,
            new FixedEnvelopeFactory("message-1"),
            new SqsQueueOptions(QueueUrl, SqsQueueKind.Fifo, messageGroupId: "group-1"));

        await publisher.PublishAsync("hello");

        var request = Assert.Single(client.SendRequests);
        Assert.Equal("group-1", request.MessageGroupId);
        Assert.Equal("message-1", request.MessageDeduplicationId);
    }

    [Fact]
    public async Task RequestClient_DuplicateCorrelationId_ReturnsFailureAndDoesNotSendSecondRequest()
    {
        var client = new FakeSqsClient { BlockWhenNoMessages = true };
        var requestClient = new SqsRequestClient<string, string>(
            client,
            new FixedEnvelopeFactory("duplicate-id"),
            new StubSerializer(),
            timeoutPolicy: null,
            new SqsRequestReplyOptions(RequestQueueUrl, ReplyQueueUrl, TimeSpan.FromSeconds(5)));

        var first = requestClient.SendAsync("first");
        await WaitUntilAsync(() => client.SendRequests.Count == 1);

        var second = await requestClient.SendAsync("second");

        Assert.Equal(ReplyStatus.Failure, second.Status);
        Assert.Contains("Duplicate correlation ID", second.Error, StringComparison.Ordinal);
        Assert.Single(client.SendRequests);

        await requestClient.DisposeAsync();
        await first;
    }

    [Fact]
    public async Task RequestClient_MatchedReply_DeletesReplyAndReturnsValue()
    {
        var client = new FakeSqsClient();
        var requestClient = new SqsRequestClient<string, string>(
            client,
            new FixedEnvelopeFactory("request-1"),
            new StubSerializer(),
            timeoutPolicy: null,
            new SqsRequestReplyOptions(RequestQueueUrl, ReplyQueueUrl, TimeSpan.FromSeconds(2)));
        client.OnSend = _ => client.EnqueueReceiveBatch(CreateReplyMessage("reply-1", "request-1", "reply-value"));

        var reply = await requestClient.SendAsync("request");

        Assert.Equal(ReplyStatus.Success, reply.Status);
        Assert.Equal("reply-value", reply.Value);
        var delete = Assert.Single(client.DeleteRequests);
        Assert.Equal(ReplyQueueUrl, delete.QueueUrl);
        Assert.Equal("reply-receipt-reply-1", delete.ReceiptHandle);

        await requestClient.DisposeAsync();
    }

    [Fact]
    public async Task RequestClient_UnmatchedMalformedReply_DeletesWithoutDecodingBody()
    {
        var client = new FakeSqsClient();
        client.EnqueueReceiveBatch(new SqsMessage
        {
            MessageId = "unmatched",
            ReceiptHandle = "unmatched-receipt",
            Body = "not-base64",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>(),
        });

        var requestClient = new SqsRequestClient<string, string>(
            client,
            new FixedEnvelopeFactory("request-1"),
            new StubSerializer(),
            timeoutPolicy: null,
            new SqsRequestReplyOptions(RequestQueueUrl, ReplyQueueUrl, TimeSpan.FromSeconds(2)));

        await WaitUntilAsync(() => client.DeleteRequests.Count == 1);
        await requestClient.DisposeAsync();

        var delete = Assert.Single(client.DeleteRequests);
        Assert.Equal(ReplyQueueUrl, delete.QueueUrl);
        Assert.Equal("unmatched-receipt", delete.ReceiptHandle);
    }

    [Fact]
    public async Task RequestClient_MatchedMalformedReply_DeletesAndReturnsFailure()
    {
        var client = new FakeSqsClient();
        var requestClient = new SqsRequestClient<string, string>(
            client,
            new FixedEnvelopeFactory("request-1"),
            new StubSerializer(),
            timeoutPolicy: null,
            new SqsRequestReplyOptions(RequestQueueUrl, ReplyQueueUrl, TimeSpan.FromSeconds(2)));
        client.OnSend = _ => client.EnqueueReceiveBatch(new SqsMessage
        {
            MessageId = "malformed",
            ReceiptHandle = "malformed-receipt",
            Body = "not-base64",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["liaison.correlation-id"] = new MessageAttributeValue
                {
                    DataType = "String",
                    StringValue = "request-1",
                },
            },
        });

        var reply = await requestClient.SendAsync("request");

        Assert.Equal(ReplyStatus.Failure, reply.Status);
        Assert.Contains("Base-64", reply.Error, StringComparison.Ordinal);
        var delete = Assert.Single(client.DeleteRequests);
        Assert.Equal(ReplyQueueUrl, delete.QueueUrl);
        Assert.Equal("malformed-receipt", delete.ReceiptHandle);

        await requestClient.DisposeAsync();
    }

    [Fact]
    public async Task RequestProcessor_OnHandlerSuccess_SendsCorrelatedReplyAndDeletesRequest()
    {
        var client = new FakeSqsClient { BlockWhenNoMessages = true };
        var requestEnvelope = new MessageEnvelope(
            "request-message-id",
            "correlation-1",
            DateTimeOffset.UtcNow,
            Encoding.UTF8.GetBytes("request"),
            new Dictionary<string, string>());
        client.EnqueueReceiveBatch(new SqsMessage
        {
            MessageId = "sqs-message-id",
            ReceiptHandle = "receipt-1",
            Body = SqsEnvelopeMapper.ToSqsMessageBody(requestEnvelope),
            MessageAttributes = SqsEnvelopeMapper.ToSqsMessageAttributes(requestEnvelope),
        });

        var processor = new SqsRequestProcessor<string, string>(
            client,
            new StubSerializer(),
            new StubContextFactory(),
            new SqsRequestReplyOptions(RequestQueueUrl, ReplyQueueUrl, TimeSpan.FromSeconds(5)),
            new StubRequestHandler());

        await processor.StartAsync();
        await WaitUntilAsync(() => client.SendRequests.Count == 1 && client.DeleteRequests.Count == 1);
        await processor.DisposeAsync();

        var reply = Assert.Single(client.SendRequests);
        Assert.Equal(ReplyQueueUrl, reply.QueueUrl);
        Assert.True(reply.MessageAttributes.TryGetValue("liaison.correlation-id", out var correlationAttribute));
        Assert.Equal("request-message-id", correlationAttribute.StringValue);

        var delete = Assert.Single(client.DeleteRequests);
        Assert.Equal(RequestQueueUrl, delete.QueueUrl);
        Assert.Equal("receipt-1", delete.ReceiptHandle);
    }

    [Fact]
    public async Task RequestProcessor_ArgumentException_SendsValidationErrorAndDeletesRequest()
    {
        var client = new FakeSqsClient { BlockWhenNoMessages = true };
        client.EnqueueReceiveBatch(CreateRequestMessage("request-message-id", "receipt-1", "request"));
        var processor = CreateProcessor(client, new ThrowingRequestHandler(new ArgumentException("invalid")));

        await processor.StartAsync();
        await WaitUntilAsync(() => client.SendRequests.Count == 1 && client.DeleteRequests.Count == 1);
        await processor.DisposeAsync();

        var reply = Assert.Single(client.SendRequests);
        Assert.Equal("ValidationError", reply.MessageAttributes["reply.status"].StringValue);
        Assert.Equal("invalid", reply.MessageAttributes["reply.error"].StringValue);
        Assert.Single(client.DeleteRequests);
        Assert.Empty(client.ChangeVisibilityRequests);
    }

    [Fact]
    public async Task RequestProcessor_GenericException_SendsFailureAndDeletesRequest()
    {
        var client = new FakeSqsClient { BlockWhenNoMessages = true };
        client.EnqueueReceiveBatch(CreateRequestMessage("request-message-id", "receipt-1", "request"));
        var processor = CreateProcessor(client, new ThrowingRequestHandler(new InvalidOperationException("boom")));

        await processor.StartAsync();
        await WaitUntilAsync(() => client.SendRequests.Count == 1 && client.DeleteRequests.Count == 1);
        await processor.DisposeAsync();

        var reply = Assert.Single(client.SendRequests);
        Assert.Equal("Failure", reply.MessageAttributes["reply.status"].StringValue);
        Assert.Equal("Request processing failed.", reply.MessageAttributes["reply.error"].StringValue);
        Assert.Single(client.DeleteRequests);
        Assert.Empty(client.ChangeVisibilityRequests);
    }

    [Fact]
    public async Task RequestProcessor_ReplySendFails_AbandonsRequest()
    {
        var client = new FakeSqsClient { BlockWhenNoMessages = true };
        client.EnqueueReceiveBatch(CreateRequestMessage("request-message-id", "receipt-1", "request"));
        client.SendExceptionFactory = request =>
            request.QueueUrl == ReplyQueueUrl ? new InvalidOperationException("send failed") : null;
        var processor = CreateProcessor(client, new StubRequestHandler());

        await processor.StartAsync();
        await WaitUntilAsync(() => client.ChangeVisibilityRequests.Count == 1);
        await processor.DisposeAsync();

        Assert.Empty(client.DeleteRequests);
        var abandon = Assert.Single(client.ChangeVisibilityRequests);
        Assert.Equal(RequestQueueUrl, abandon.QueueUrl);
        Assert.Equal("receipt-1", abandon.ReceiptHandle);
        Assert.Equal(0, abandon.VisibilityTimeout);
    }

    [Fact]
    public async Task Subscription_SuccessDeletesAndFailureAbandons()
    {
        var successClient = new FakeSqsClient { BlockWhenNoMessages = true };
        successClient.EnqueueReceiveBatch(CreateRequestMessage("message-1", "receipt-success", "message"));
        var successSubscription = new SqsSubscription<string>(
            successClient,
            new StubSerializer(),
            new StubContextFactory(),
            new SqsQueueOptions(QueueUrl),
            new StubMessageHandler());

        await successSubscription.StartAsync();
        await WaitUntilAsync(() => successClient.DeleteRequests.Count == 1);
        await successSubscription.DisposeAsync();

        Assert.Single(successClient.DeleteRequests);
        Assert.Empty(successClient.ChangeVisibilityRequests);

        var failureClient = new FakeSqsClient { BlockWhenNoMessages = true };
        failureClient.EnqueueReceiveBatch(CreateRequestMessage("message-2", "receipt-failure", "message"));
        var failureSubscription = new SqsSubscription<string>(
            failureClient,
            new StubSerializer(),
            new StubContextFactory(),
            new SqsQueueOptions(QueueUrl),
            new ThrowingMessageHandler());

        await failureSubscription.StartAsync();
        await WaitUntilAsync(() => failureClient.ChangeVisibilityRequests.Count == 1);
        await failureSubscription.DisposeAsync();

        Assert.Empty(failureClient.DeleteRequests);
        var abandon = Assert.Single(failureClient.ChangeVisibilityRequests);
        Assert.Equal(QueueUrl, abandon.QueueUrl);
        Assert.Equal("receipt-failure", abandon.ReceiptHandle);
        Assert.Equal(0, abandon.VisibilityTimeout);
    }

    [Fact]
    public async Task DependencyInjection_ResolvesPublisherAndRequestClient()
    {
        var client = new FakeSqsClient { BlockWhenNoMessages = true };
        var services = new ServiceCollection();
        services.AddSingleton<IMessageSerializer, StubSerializer>();
        services.AddSingleton<IMessageEnvelopeFactory>(new FixedEnvelopeFactory("message-1"));
        services.AddSqsClient(client);
        services.AddSqsPublisher<string>(options => options.QueueUrl = QueueUrl);
        services.AddSqsRequestClient<string, string>(options =>
        {
            options.RequestQueueUrl = RequestQueueUrl;
            options.ReplyQueueUrl = ReplyQueueUrl;
            options.DefaultTimeout = TimeSpan.FromSeconds(5);
        });

        await using var provider = services.BuildServiceProvider();

        Assert.IsType<SqsPublisher<string>>(provider.GetRequiredService<IMessagePublisher<string>>());
        Assert.IsType<SqsRequestClient<string, string>>(provider.GetRequiredService<IRequestClient<string, string>>());
    }

    [Fact]
    public async Task HostingSubscriptionService_StartsAndStopsSubscription()
    {
        var client = new FakeSqsClient { BlockWhenNoMessages = true };
        var subscription = new SqsSubscription<string>(
            client,
            new StubSerializer(),
            new StubContextFactory(),
            new SqsQueueOptions(QueueUrl),
            new StubMessageHandler());
        var service = new SqsSubscriptionService<string>(subscription);

        await service.StartAsync(CancellationToken.None);
        await client.ReceiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await service.StopAsync(CancellationToken.None);
        await client.ReceiveCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotEmpty(client.ReceiveRequests);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.True(predicate());
    }

    private static SqsMessage CreateReplyMessage(string messageId, string correlationId, string payload)
    {
        var envelope = new MessageEnvelope(
            messageId,
            correlationId,
            DateTimeOffset.UtcNow,
            Encoding.UTF8.GetBytes(payload),
            new Dictionary<string, string>());

        return new SqsMessage
        {
            MessageId = $"sqs-{messageId}",
            ReceiptHandle = $"reply-receipt-{messageId}",
            Body = SqsEnvelopeMapper.ToSqsMessageBody(envelope),
            MessageAttributes = SqsEnvelopeMapper.ToSqsMessageAttributes(envelope),
        };
    }

    private static SqsMessage CreateRequestMessage(string messageId, string receiptHandle, string payload)
    {
        var envelope = new MessageEnvelope(
            messageId,
            correlationId: null,
            DateTimeOffset.UtcNow,
            Encoding.UTF8.GetBytes(payload),
            new Dictionary<string, string>());

        return new SqsMessage
        {
            MessageId = $"sqs-{messageId}",
            ReceiptHandle = receiptHandle,
            Body = SqsEnvelopeMapper.ToSqsMessageBody(envelope),
            MessageAttributes = SqsEnvelopeMapper.ToSqsMessageAttributes(envelope),
        };
    }

    private static SqsRequestProcessor<string, string> CreateProcessor(
        FakeSqsClient client,
        IRequestHandler<string, string> handler)
    {
        return new SqsRequestProcessor<string, string>(
            client,
            new StubSerializer(),
            new StubContextFactory(),
            new SqsRequestReplyOptions(RequestQueueUrl, ReplyQueueUrl, TimeSpan.FromSeconds(5)),
            handler);
    }

    private sealed class FakeSqsClient : IAmazonSQS
    {
        private readonly ConcurrentQueue<IReadOnlyList<SqsMessage>> _receiveBatches = new();

        public ConcurrentQueue<SendMessageRequest> SendRequests { get; } = new();

        public ConcurrentQueue<ReceiveMessageRequest> ReceiveRequests { get; } = new();

        public ConcurrentQueue<DeleteMessageRequest> DeleteRequests { get; } = new();

        public ConcurrentQueue<ChangeMessageVisibilityRequest> ChangeVisibilityRequests { get; } = new();

        public TaskCompletionSource ReceiveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReceiveCanceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool BlockWhenNoMessages { get; set; }

        public Action<SendMessageRequest>? OnSend { get; set; }

        public Func<SendMessageRequest, Exception?>? SendExceptionFactory { get; set; }

        public IClientConfig Config { get; } = new AmazonSQSConfig { ServiceURL = "http://localhost:1" };

        public ISQSPaginatorFactory Paginators => throw new NotImplementedException();

        public void EnqueueReceiveBatch(params SqsMessage[] messages)
        {
            _receiveBatches.Enqueue(messages);
        }

        public Task<SendMessageResponse> SendMessageAsync(
            SendMessageRequest request,
            CancellationToken cancellationToken = default)
        {
            var exception = SendExceptionFactory?.Invoke(request);
            if (exception is not null)
            {
                throw exception;
            }

            SendRequests.Enqueue(request);
            OnSend?.Invoke(request);
            return Task.FromResult(new SendMessageResponse());
        }

        public Task<ReceiveMessageResponse> ReceiveMessageAsync(
            ReceiveMessageRequest request,
            CancellationToken cancellationToken = default)
        {
            ReceiveRequests.Enqueue(request);
            ReceiveStarted.TrySetResult();

            if (_receiveBatches.TryDequeue(out var messages))
            {
                return Task.FromResult(new ReceiveMessageResponse
                {
                    Messages = messages.ToList(),
                });
            }

            if (!BlockWhenNoMessages)
            {
                return Task.FromResult(new ReceiveMessageResponse
                {
                    Messages = new List<SqsMessage>(),
                });
            }

            var response = new TaskCompletionSource<ReceiveMessageResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            var registration = cancellationToken.Register(
                static state => ((TaskCompletionSource<ReceiveMessageResponse>)state!).TrySetCanceled(),
                response);
            response.Task.ContinueWith(
                _ => registration.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            cancellationToken.Register(() => ReceiveCanceled.TrySetResult());
            return response.Task;
        }

        public Task<DeleteMessageResponse> DeleteMessageAsync(
            DeleteMessageRequest request,
            CancellationToken cancellationToken = default)
        {
            DeleteRequests.Enqueue(request);
            return Task.FromResult(new DeleteMessageResponse());
        }

        public Task<ChangeMessageVisibilityResponse> ChangeMessageVisibilityAsync(
            ChangeMessageVisibilityRequest request,
            CancellationToken cancellationToken = default)
        {
            ChangeVisibilityRequests.Enqueue(request);
            return Task.FromResult(new ChangeMessageVisibilityResponse());
        }

        public void Dispose()
        {
        }

        public Task<Dictionary<string, string>> GetAttributesAsync(string queueUrl)
            => throw new NotImplementedException();

        public Task SetAttributesAsync(string queueUrl, Dictionary<string, string> attributes)
            => throw new NotImplementedException();

        public Task<AddPermissionResponse> AddPermissionAsync(
            string queueUrl,
            string label,
            List<string> awsAccountIds,
            List<string> actions,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<AddPermissionResponse> AddPermissionAsync(
            AddPermissionRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<string> AuthorizeS3ToSendMessageAsync(string queueUrl, string bucket)
            => throw new NotImplementedException();

        public Task<CancelMessageMoveTaskResponse> CancelMessageMoveTaskAsync(
            CancelMessageMoveTaskRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ChangeMessageVisibilityResponse> ChangeMessageVisibilityAsync(
            string queueUrl,
            string receiptHandle,
            int visibilityTimeout,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ChangeMessageVisibilityBatchResponse> ChangeMessageVisibilityBatchAsync(
            string queueUrl,
            List<ChangeMessageVisibilityBatchRequestEntry> entries,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ChangeMessageVisibilityBatchResponse> ChangeMessageVisibilityBatchAsync(
            ChangeMessageVisibilityBatchRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<CreateQueueResponse> CreateQueueAsync(
            string queueName,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<CreateQueueResponse> CreateQueueAsync(
            CreateQueueRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<DeleteMessageResponse> DeleteMessageAsync(
            string queueUrl,
            string receiptHandle,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<DeleteMessageBatchResponse> DeleteMessageBatchAsync(
            string queueUrl,
            List<DeleteMessageBatchRequestEntry> entries,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<DeleteMessageBatchResponse> DeleteMessageBatchAsync(
            DeleteMessageBatchRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<DeleteQueueResponse> DeleteQueueAsync(
            string queueUrl,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<DeleteQueueResponse> DeleteQueueAsync(
            DeleteQueueRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Amazon.Runtime.Endpoints.Endpoint DetermineServiceOperationEndpoint(AmazonWebServiceRequest request)
            => throw new NotImplementedException();

        public Task<GetQueueAttributesResponse> GetQueueAttributesAsync(
            string queueUrl,
            List<string> attributeNames,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<GetQueueAttributesResponse> GetQueueAttributesAsync(
            GetQueueAttributesRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<GetQueueUrlResponse> GetQueueUrlAsync(
            string queueName,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<GetQueueUrlResponse> GetQueueUrlAsync(
            GetQueueUrlRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ListDeadLetterSourceQueuesResponse> ListDeadLetterSourceQueuesAsync(
            ListDeadLetterSourceQueuesRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ListMessageMoveTasksResponse> ListMessageMoveTasksAsync(
            ListMessageMoveTasksRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ListQueuesResponse> ListQueuesAsync(
            string queueNamePrefix,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ListQueuesResponse> ListQueuesAsync(
            ListQueuesRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ListQueueTagsResponse> ListQueueTagsAsync(
            ListQueueTagsRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<PurgeQueueResponse> PurgeQueueAsync(
            string queueUrl,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<PurgeQueueResponse> PurgeQueueAsync(
            PurgeQueueRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ReceiveMessageResponse> ReceiveMessageAsync(
            string queueUrl,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<RemovePermissionResponse> RemovePermissionAsync(
            string queueUrl,
            string label,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<RemovePermissionResponse> RemovePermissionAsync(
            RemovePermissionRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<SendMessageResponse> SendMessageAsync(
            string queueUrl,
            string messageBody,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<SendMessageBatchResponse> SendMessageBatchAsync(
            string queueUrl,
            List<SendMessageBatchRequestEntry> entries,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<SendMessageBatchResponse> SendMessageBatchAsync(
            SendMessageBatchRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<SetQueueAttributesResponse> SetQueueAttributesAsync(
            string queueUrl,
            Dictionary<string, string> attributes,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<SetQueueAttributesResponse> SetQueueAttributesAsync(
            SetQueueAttributesRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StartMessageMoveTaskResponse> StartMessageMoveTaskAsync(
            StartMessageMoveTaskRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<TagQueueResponse> TagQueueAsync(
            TagQueueRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<UntagQueueResponse> UntagQueueAsync(
            UntagQueueRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class CapturingLargePayloadPolicy : ILargePayloadPolicy
    {
        public int ThresholdBytes => int.MaxValue;

        public bool UseCompression => false;

        public int PrepareOutboundCallCount { get; private set; }

        public int ResolveInboundCallCount { get; private set; }

        public Task<MessageEnvelope> PrepareOutboundAsync(
            MessageEnvelope envelope,
            IPayloadStore store,
            DateTimeOffset? expiresAtUtc = null,
            CancellationToken ct = default)
        {
            PrepareOutboundCallCount++;
            return Task.FromResult(envelope);
        }

        public Task<MessageEnvelope> ResolveInboundAsync(
            MessageEnvelope envelope,
            IPayloadStore store,
            CancellationToken ct = default)
        {
            ResolveInboundCallCount++;
            return Task.FromResult(envelope);
        }
    }

    private sealed class NoOpPayloadStore : IPayloadStore
    {
        public Task<string> UploadAsync(
            Stream payload,
            string keyPrefix,
            long? sizeHintBytes = null,
            DateTimeOffset? expiresAtUtc = null,
            CancellationToken ct = default)
            => Task.FromResult("ref-noop");

        public Task<Stream> DownloadAsync(string reference, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(string reference, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FixedEnvelopeFactory : IMessageEnvelopeFactory
    {
        private readonly string _messageId;

        public FixedEnvelopeFactory(string messageId)
        {
            _messageId = messageId;
        }

        public MessageEnvelope Create<T>(
            T message,
            IReadOnlyDictionary<string, string>? headers = null,
            string? correlationId = null,
            DateTimeOffset? sentAtUtc = null)
        {
            return new MessageEnvelope(
                _messageId,
                correlationId,
                sentAtUtc ?? DateTimeOffset.UtcNow,
                Encoding.UTF8.GetBytes(message?.ToString() ?? string.Empty),
                headers ?? new Dictionary<string, string>());
        }
    }

    private sealed class StubSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value)
            => Encoding.UTF8.GetBytes(value?.ToString() ?? string.Empty);

        public T Deserialize<T>(ReadOnlyMemory<byte> payload)
            => (T)(object)Encoding.UTF8.GetString(payload.Span);
    }

    private sealed class StubContextFactory : IMessageContextFactory
    {
        public MessageContext Create(MessageEnvelope envelope)
            => new MessageContext(envelope.MessageId, envelope.CorrelationId, envelope.Headers);
    }

    private sealed class StubMessageHandler : IMessageHandler<string>
    {
        public Task HandleAsync(string message, MessageContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class ThrowingMessageHandler : IMessageHandler<string>
    {
        public Task HandleAsync(string message, MessageContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("handler failed");
    }

    private sealed class StubRequestHandler : IRequestHandler<string, string>
    {
        public Task<string> HandleAsync(string request, MessageContext context, CancellationToken cancellationToken)
            => Task.FromResult("reply");
    }

    private sealed class ThrowingRequestHandler : IRequestHandler<string, string>
    {
        private readonly Exception _exception;

        public ThrowingRequestHandler(Exception exception)
        {
            _exception = exception;
        }

        public Task<string> HandleAsync(string request, MessageContext context, CancellationToken cancellationToken)
            => throw _exception;
    }
}
