namespace Liaison.Messaging;

using System;

/// <summary>
/// Represents a payload write failure because the target reference already exists.
/// </summary>
public sealed class PayloadAlreadyExistsException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadAlreadyExistsException"/> type.
    /// </summary>
    /// <param name="reference">The payload reference that already exists.</param>
    public PayloadAlreadyExistsException(string reference)
        : this(reference, $"Payload reference '{reference}' already exists.", innerException: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadAlreadyExistsException"/> type.
    /// </summary>
    /// <param name="reference">The payload reference that already exists.</param>
    /// <param name="innerException">The underlying exception.</param>
    public PayloadAlreadyExistsException(string reference, Exception innerException)
        : this(reference, $"Payload reference '{reference}' already exists.", innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadAlreadyExistsException"/> type.
    /// </summary>
    /// <param name="reference">The payload reference that already exists.</param>
    /// <param name="message">Exception message.</param>
    /// <param name="innerException">The underlying exception.</param>
    public PayloadAlreadyExistsException(string reference, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        if (reference is null)
        {
            throw new ArgumentNullException(nameof(reference));
        }

        Reference = reference;
    }

    /// <summary>
    /// Gets the payload reference that already exists.
    /// </summary>
    public string Reference { get; }
}
