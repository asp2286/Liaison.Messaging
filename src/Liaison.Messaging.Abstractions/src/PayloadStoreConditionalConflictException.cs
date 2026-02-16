namespace Liaison.Messaging;

using System;

/// <summary>
/// Represents a conditional-write conflict where the storage service could not apply atomic preconditions.
/// </summary>
public sealed class PayloadStoreConditionalConflictException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadStoreConditionalConflictException"/> type.
    /// </summary>
    /// <param name="reference">The payload reference associated with the conflict.</param>
    public PayloadStoreConditionalConflictException(string reference)
        : this(reference, $"Conditional write conflict for payload reference '{reference}'.", innerException: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadStoreConditionalConflictException"/> type.
    /// </summary>
    /// <param name="reference">The payload reference associated with the conflict.</param>
    /// <param name="innerException">The underlying exception.</param>
    public PayloadStoreConditionalConflictException(string reference, Exception innerException)
        : this(reference, $"Conditional write conflict for payload reference '{reference}'.", innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadStoreConditionalConflictException"/> type.
    /// </summary>
    /// <param name="reference">The payload reference associated with the conflict.</param>
    /// <param name="message">Exception message.</param>
    /// <param name="innerException">The underlying exception.</param>
    public PayloadStoreConditionalConflictException(string reference, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        if (reference is null)
        {
            throw new ArgumentNullException(nameof(reference));
        }

        Reference = reference;
    }

    /// <summary>
    /// Gets the payload reference associated with the conflict.
    /// </summary>
    public string Reference { get; }
}
