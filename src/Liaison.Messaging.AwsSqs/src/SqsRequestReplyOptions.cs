namespace Liaison.Messaging.AwsSqs;

using System;
using System.Threading;

/// <summary>
/// Defines Amazon SQS request/reply queue settings.
/// </summary>
/// <remarks>
/// SQS allows 10 message attributes and 256 KiB total message size. Liaison request,
/// reply, and correlation attributes consume part of that budget, and base64 body
/// encoding raises payload size by about 33%; configure large-payload externalization
/// below roughly 192 KiB. SQS has no per-message dead-letter operation; use queue
/// redrive policies for unprocessable request messages.
/// </remarks>
public sealed record SqsRequestReplyOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SqsRequestReplyOptions"/> type
    /// with default values for use with the <c>Action&lt;SqsRequestReplyOptions&gt;</c>
    /// configuration pattern.
    /// </summary>
    public SqsRequestReplyOptions()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqsRequestReplyOptions"/> type.
    /// </summary>
    /// <param name="requestQueueUrl">The full Amazon SQS request queue URL.</param>
    /// <param name="replyQueueUrl">The full Amazon SQS reply queue URL.</param>
    /// <param name="defaultTimeout">Default request timeout when no timeout policy is provided.</param>
    /// <exception cref="ArgumentException">Thrown when queue URLs are empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="defaultTimeout"/> is invalid.</exception>
    public SqsRequestReplyOptions(
        string requestQueueUrl,
        string replyQueueUrl,
        TimeSpan defaultTimeout)
    {
        RequestQueueUrl = requestQueueUrl;
        ReplyQueueUrl = replyQueueUrl;
        DefaultTimeout = defaultTimeout;
        Validate();
    }

    /// <summary>
    /// Gets or sets the full Amazon SQS request queue URL.
    /// </summary>
    public string RequestQueueUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full Amazon SQS reply queue URL.
    /// </summary>
    public string ReplyQueueUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the default request timeout.
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; }

    /// <summary>
    /// Gets or sets the queue kind for request and reply sends.
    /// </summary>
    public SqsQueueKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the FIFO message group identifier.
    /// </summary>
    public string? MessageGroupId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether FIFO queues use content-based deduplication.
    /// </summary>
    public bool UseContentBasedDeduplication { get; set; }

    /// <summary>
    /// Gets or sets the long-poll wait time in seconds.
    /// </summary>
    public int WaitTimeSeconds { get; set; } = 20;

    /// <summary>
    /// Gets or sets the maximum number of messages to receive per poll.
    /// </summary>
    public int MaxNumberOfMessages { get; set; } = 10;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(RequestQueueUrl))
        {
            throw new ArgumentException("RequestQueueUrl must be provided.");
        }

        if (string.IsNullOrWhiteSpace(ReplyQueueUrl))
        {
            throw new ArgumentException("ReplyQueueUrl must be provided.");
        }

        if (DefaultTimeout < TimeSpan.Zero && DefaultTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(DefaultTimeout), "DefaultTimeout must be non-negative or infinite.");
        }

        SqsQueueOptions.ValidateReceiveSettings(WaitTimeSeconds, MaxNumberOfMessages);

        if (Kind == SqsQueueKind.Fifo && string.IsNullOrWhiteSpace(MessageGroupId))
        {
            throw new ArgumentException("MessageGroupId must be provided for FIFO queues.");
        }
    }
}
