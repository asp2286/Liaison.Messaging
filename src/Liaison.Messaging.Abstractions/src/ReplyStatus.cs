namespace Liaison.Messaging;

/// <summary>
/// Represents status codes for request/reply outcomes.
/// </summary>
public enum ReplyStatus
{
    /// <summary>
    /// The operation completed successfully.
    /// </summary>
    Success,

    /// <summary>
    /// The operation failed due to invalid input.
    /// </summary>
    ValidationError,

    /// <summary>
    /// The operation failed for a non-validation reason.
    /// </summary>
    Failure,

    /// <summary>
    /// The operation timed out before completion.
    /// </summary>
    Timeout,
}
