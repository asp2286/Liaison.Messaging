namespace Liaison.Messaging;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Defines a transport-agnostic store for externally persisted message payloads.
/// </summary>
public interface IPayloadStore
{
    /// <summary>
    /// Uploads a payload stream and returns an opaque reference that can be used to download it later.
    /// </summary>
    /// <param name="payload">Payload stream to upload.</param>
    /// <param name="keyPrefix">Provider-agnostic key prefix used for reference generation.</param>
    /// <param name="sizeHintBytes">Optional payload size hint, in bytes.</param>
    /// <param name="expiresAtUtc">Optional UTC expiry timestamp for the stored payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An opaque payload reference.</returns>
    Task<string> UploadAsync(
        Stream payload,
        string keyPrefix,
        long? sizeHintBytes = null,
        DateTimeOffset? expiresAtUtc = null,
        CancellationToken ct = default);

    /// <summary>
    /// Downloads a previously uploaded payload stream by reference.
    /// </summary>
    /// <param name="reference">Opaque payload reference.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A readable stream containing the payload bytes.</returns>
    Task<Stream> DownloadAsync(string reference, CancellationToken ct = default);

    /// <summary>
    /// Deletes a stored payload by reference.
    /// </summary>
    /// <param name="reference">Opaque payload reference.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(string reference, CancellationToken ct = default);
}
