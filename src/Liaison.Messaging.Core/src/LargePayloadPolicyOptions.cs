namespace Liaison.Messaging;

/// <summary>
/// Options for configuring default large-payload policy behavior.
/// </summary>
/// <param name="ThresholdBytes">Payload size threshold for externalization, in bytes.</param>
/// <param name="UseCompression">Whether outbound externalized payload uploads use gzip compression.</param>
/// <param name="KeyPrefix">Payload store key prefix used when uploading externalized payloads.</param>
public sealed record LargePayloadPolicyOptions(
    int ThresholdBytes = 200 * 1024,
    bool UseCompression = false,
    string KeyPrefix = "payload");
