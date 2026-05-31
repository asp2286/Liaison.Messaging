namespace Liaison.Messaging.AwsSqs;

using System;

/// <summary>
/// Defines Amazon SQS queue settings for publish and subscribe operations.
/// </summary>
/// <remarks>
/// SQS allows 10 message attributes and 256 KiB total message size. Liaison semantic
/// attributes consume part of that budget, and base64 body encoding raises payload
/// size by about 33%; configure large-payload externalization below roughly 192 KiB.
/// SQS retries abandoned messages by visibility timeout, so configure a queue redrive
/// policy to avoid hot retry loops for unprocessable messages.
/// </remarks>
public sealed record SqsQueueOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SqsQueueOptions"/> type
    /// with default values for use with the <c>Action&lt;SqsQueueOptions&gt;</c>
    /// configuration pattern.
    /// </summary>
    public SqsQueueOptions()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqsQueueOptions"/> type.
    /// </summary>
    /// <param name="queueUrl">The full Amazon SQS queue URL.</param>
    /// <param name="kind">The queue kind.</param>
    /// <param name="messageGroupId">The FIFO message group identifier.</param>
    /// <param name="useContentBasedDeduplication">Whether the FIFO queue uses content-based deduplication.</param>
    /// <exception cref="ArgumentException">Thrown when required settings are invalid.</exception>
    public SqsQueueOptions(
        string queueUrl,
        SqsQueueKind kind = SqsQueueKind.Standard,
        string? messageGroupId = null,
        bool useContentBasedDeduplication = false)
    {
        QueueUrl = queueUrl;
        Kind = kind;
        MessageGroupId = messageGroupId;
        UseContentBasedDeduplication = useContentBasedDeduplication;
        Validate();
    }

    /// <summary>
    /// Gets or sets the full Amazon SQS queue URL.
    /// </summary>
    public string QueueUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the queue kind.
    /// </summary>
    public SqsQueueKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the FIFO message group identifier.
    /// </summary>
    public string? MessageGroupId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a FIFO queue uses content-based deduplication.
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

    /// <summary>
    /// Gets or sets the receive visibility timeout in seconds. When <see langword="null"/>, the queue default is used.
    /// </summary>
    public int? VisibilityTimeoutSeconds { get; set; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(QueueUrl))
        {
            throw new ArgumentException("QueueUrl must be provided.");
        }

        ValidateReceiveSettings(WaitTimeSeconds, MaxNumberOfMessages, VisibilityTimeoutSeconds);

        if (Kind == SqsQueueKind.Fifo && string.IsNullOrWhiteSpace(MessageGroupId))
        {
            throw new ArgumentException("MessageGroupId must be provided for FIFO queues.");
        }
    }

    internal static void ValidateReceiveSettings(
        int waitTimeSeconds,
        int maxNumberOfMessages,
        int? visibilityTimeoutSeconds = null)
    {
        if (waitTimeSeconds < 1 || waitTimeSeconds > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(waitTimeSeconds), "WaitTimeSeconds must be between 1 and 20.");
        }

        if (maxNumberOfMessages < 1 || maxNumberOfMessages > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(maxNumberOfMessages), "MaxNumberOfMessages must be between 1 and 10.");
        }

        if (visibilityTimeoutSeconds.HasValue && visibilityTimeoutSeconds.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeoutSeconds), "VisibilityTimeoutSeconds must be greater than or equal to zero.");
        }
    }
}
