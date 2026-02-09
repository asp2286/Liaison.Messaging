namespace Liaison.Messaging.AzureServiceBus;

/// <summary>
/// Defines optional semantic header keys and values for Azure Service Bus routing.
/// </summary>
public static class AzureServiceBusSemanticHeaders
{
    /// <summary>
    /// Header key used to express optional semantic kind information.
    /// </summary>
    public const string Kind = "liaison.kind";

    /// <summary>
    /// Optional semantic kind value for fan-out notifications.
    /// </summary>
    public const string KindEvent = "event";

    /// <summary>
    /// Optional semantic kind value for queue-targeted commands.
    /// </summary>
    public const string KindCommand = "command";

    /// <summary>
    /// Optional semantic kind value for request/reply requests.
    /// </summary>
    public const string KindRequest = "request";
}
