namespace Liaison.Messaging;

using System;

/// <summary>
/// Represents a transient or infrastructure-level payload store availability failure.
/// </summary>
public sealed class PayloadStoreUnavailableException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadStoreUnavailableException"/> type.
    /// </summary>
    public PayloadStoreUnavailableException()
        : base("Payload store is unavailable.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadStoreUnavailableException"/> type.
    /// </summary>
    /// <param name="message">Exception message.</param>
    public PayloadStoreUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadStoreUnavailableException"/> type.
    /// </summary>
    /// <param name="message">Exception message.</param>
    /// <param name="innerException">The underlying exception.</param>
    public PayloadStoreUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
