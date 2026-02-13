namespace Liaison.Messaging;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Applies large-payload externalization policies to outbound and inbound message envelopes.
/// </summary>
public interface ILargePayloadPolicy
{
    /// <summary>
    /// Gets the byte-size threshold beyond which outbound payloads are externalized.
    /// </summary>
    int ThresholdBytes { get; }

    /// <summary>
    /// Gets a value indicating whether externalized payload uploads are gzip-compressed.
    /// </summary>
    bool UseCompression { get; }

    /// <summary>
    /// Prepares an outbound envelope for transport by externalizing payloads over the configured threshold.
    /// </summary>
    /// <param name="envelope">Outbound envelope.</param>
    /// <param name="store">Payload store used for externalization.</param>
    /// <param name="expiresAtUtc">Optional payload expiry timestamp.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A new envelope instance containing inline or externalized payload metadata.</returns>
    Task<MessageEnvelope> PrepareOutboundAsync(
        MessageEnvelope envelope,
        IPayloadStore store,
        DateTimeOffset? expiresAtUtc = null,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves an inbound envelope by downloading and restoring externalized payloads when required.
    /// </summary>
    /// <param name="envelope">Inbound envelope.</param>
    /// <param name="store">Payload store used for resolution.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A new envelope instance with inline payload bytes restored when external mode is used.</returns>
    Task<MessageEnvelope> ResolveInboundAsync(
        MessageEnvelope envelope,
        IPayloadStore store,
        CancellationToken ct = default);
}
