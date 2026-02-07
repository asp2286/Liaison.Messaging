namespace Liaison.Messaging;

/// <summary>
/// Generates message identifiers.
/// </summary>
public interface IMessageIdGenerator
{
    /// <summary>
    /// Creates a new message identifier.
    /// </summary>
    /// <returns>A new message identifier.</returns>
    string NewId();
}
