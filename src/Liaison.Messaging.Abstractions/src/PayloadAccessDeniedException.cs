namespace Liaison.Messaging;

using System;

/// <summary>
/// Represents an access-denied failure when operating on a payload reference.
/// </summary>
public sealed class PayloadAccessDeniedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadAccessDeniedException"/> type.
    /// </summary>
    /// <param name="reference">The payload reference associated with the failure.</param>
    public PayloadAccessDeniedException(string reference)
        : this(reference, $"Access to payload reference '{reference}' was denied.", innerException: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadAccessDeniedException"/> type.
    /// </summary>
    /// <param name="reference">The payload reference associated with the failure.</param>
    /// <param name="innerException">The underlying exception.</param>
    public PayloadAccessDeniedException(string reference, Exception innerException)
        : this(reference, $"Access to payload reference '{reference}' was denied.", innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadAccessDeniedException"/> type.
    /// </summary>
    /// <param name="reference">The payload reference associated with the failure.</param>
    /// <param name="message">Exception message.</param>
    /// <param name="innerException">The underlying exception.</param>
    public PayloadAccessDeniedException(string reference, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        if (reference is null)
        {
            throw new ArgumentNullException(nameof(reference));
        }

        Reference = reference;
    }

    /// <summary>
    /// Gets the payload reference associated with the failure.
    /// </summary>
    public string Reference { get; }
}
