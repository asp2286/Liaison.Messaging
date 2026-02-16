namespace Liaison.Messaging;

using System;

/// <summary>
/// Represents an invalid payload reference format for store operations.
/// </summary>
public sealed class PayloadReferenceInvalidException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadReferenceInvalidException"/> type.
    /// </summary>
    /// <param name="message">Exception message.</param>
    public PayloadReferenceInvalidException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadReferenceInvalidException"/> type.
    /// </summary>
    /// <param name="message">Exception message.</param>
    /// <param name="innerException">The underlying exception.</param>
    public PayloadReferenceInvalidException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
