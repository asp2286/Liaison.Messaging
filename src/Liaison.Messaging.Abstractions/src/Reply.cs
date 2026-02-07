namespace Liaison.Messaging;

/// <summary>
/// Represents a transport-agnostic request/reply result envelope.
/// </summary>
/// <typeparam name="T">The reply payload type.</typeparam>
public sealed record Reply<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Reply{T}"/> type.
    /// </summary>
    /// <param name="status">The reply status.</param>
    /// <param name="value">The reply value when successful.</param>
    /// <param name="error">The error message when unsuccessful.</param>
    public Reply(ReplyStatus status, T? value, string? error)
    {
        Status = status;
        Value = value;
        Error = error;
    }

    /// <summary>
    /// Gets the reply status.
    /// </summary>
    public ReplyStatus Status { get; }

    /// <summary>
    /// Gets the reply value when available.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Gets the error message when available.
    /// </summary>
    public string? Error { get; }
}
