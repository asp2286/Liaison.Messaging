namespace Liaison.Messaging.AwsSqs;

/// <summary>
/// Identifies the Amazon SQS queue type.
/// </summary>
public enum SqsQueueKind
{
    /// <summary>
    /// A standard Amazon SQS queue.
    /// </summary>
    Standard,

    /// <summary>
    /// A FIFO Amazon SQS queue.
    /// </summary>
    Fifo,
}
