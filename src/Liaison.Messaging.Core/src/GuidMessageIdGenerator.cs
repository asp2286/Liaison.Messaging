namespace Liaison.Messaging;

using System;

/// <summary>
/// Generates message identifiers using <see cref="Guid"/> values.
/// </summary>
public sealed class GuidMessageIdGenerator : IMessageIdGenerator
{
    /// <inheritdoc />
    public string NewId()
    {
        return Guid.NewGuid().ToString("N").ToLowerInvariant();
    }
}
