namespace Liaison.Messaging;

using System;

/// <summary>
/// Represents a lookup failure when a payload reference cannot be found.
/// </summary>
public sealed class PayloadNotFoundException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadNotFoundException"/> type.
    /// </summary>
    /// <param name="reference">The missing payload reference.</param>
    public PayloadNotFoundException(string reference)
        : this(reference, $"Payload reference '{reference}' was not found.", innerException: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadNotFoundException"/> type.
    /// </summary>
    /// <param name="reference">The missing payload reference.</param>
    /// <param name="innerException">The underlying exception.</param>
    public PayloadNotFoundException(string reference, Exception innerException)
        : this(reference, $"Payload reference '{reference}' was not found.", innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadNotFoundException"/> type.
    /// </summary>
    /// <param name="reference">The missing payload reference.</param>
    /// <param name="message">Exception message.</param>
    /// <param name="innerException">The underlying exception.</param>
    public PayloadNotFoundException(string reference, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        if (reference is null)
        {
            throw new ArgumentNullException(nameof(reference));
        }

        Reference = reference;
    }

    /// <summary>
    /// Gets the missing payload reference.
    /// </summary>
    public string Reference { get; }
}
