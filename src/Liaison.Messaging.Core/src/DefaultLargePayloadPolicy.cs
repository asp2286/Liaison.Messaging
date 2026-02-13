namespace Liaison.Messaging;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Implements explicit, deterministic large-payload externalization and restoration behavior.
/// </summary>
public sealed class DefaultLargePayloadPolicy : ILargePayloadPolicy
{
    private readonly LargePayloadPolicyOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultLargePayloadPolicy"/> type.
    /// </summary>
    /// <param name="options">Policy options.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="options"/> has invalid numeric values.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="options"/> has invalid string values.</exception>
    public DefaultLargePayloadPolicy(LargePayloadPolicyOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (_options.ThresholdBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "ThresholdBytes must be greater than or equal to zero.");
        }

        if (string.IsNullOrWhiteSpace(_options.KeyPrefix))
        {
            throw new ArgumentException("KeyPrefix must be provided.", nameof(options));
        }
    }

    /// <inheritdoc />
    public int ThresholdBytes => _options.ThresholdBytes;

    /// <inheritdoc />
    public bool UseCompression => _options.UseCompression;

    /// <inheritdoc />
    public async Task<MessageEnvelope> PrepareOutboundAsync(
        MessageEnvelope envelope,
        IPayloadStore store,
        DateTimeOffset? expiresAtUtc = null,
        CancellationToken ct = default)
    {
        if (envelope is null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        if (store is null)
        {
            throw new ArgumentNullException(nameof(store));
        }

        if (envelope.Body.Length <= ThresholdBytes)
        {
            return CopyEnvelope(envelope, envelope.Body, envelope.Headers);
        }

        var originalPayload = envelope.Body.ToArray();
        var payloadSha256 = ComputeSha256Hex(originalPayload);
        var uploadPayload = UseCompression
            ? CompressGzip(originalPayload)
            : originalPayload;

        using var uploadStream = new MemoryStream(uploadPayload, writable: false);
        var reference = await store.UploadAsync(
                uploadStream,
                $"{_options.KeyPrefix}/{envelope.MessageId}",
                sizeHintBytes: uploadPayload.LongLength,
                expiresAtUtc: expiresAtUtc?.ToUniversalTime(),
                ct: ct)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new InvalidOperationException("Payload store returned an empty reference for an externalized payload.");
        }

        var outboundHeaders = CopyHeaders(envelope.Headers);
        outboundHeaders[LargePayloadHeaders.Mode] = LargePayloadHeaders.ModeExternal;
        outboundHeaders[LargePayloadHeaders.Reference] = reference;
        outboundHeaders[LargePayloadHeaders.Sha256] = payloadSha256;
        outboundHeaders[LargePayloadHeaders.Size] = originalPayload.LongLength.ToString(CultureInfo.InvariantCulture);

        if (UseCompression)
        {
            outboundHeaders[LargePayloadHeaders.Encoding] = LargePayloadHeaders.EncodingGzip;
        }
        else
        {
            outboundHeaders.Remove(LargePayloadHeaders.Encoding);
        }

        if (expiresAtUtc.HasValue)
        {
            outboundHeaders[LargePayloadHeaders.Expires] =
                expiresAtUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }
        else
        {
            outboundHeaders.Remove(LargePayloadHeaders.Expires);
        }

        return CopyEnvelope(envelope, ReadOnlyMemory<byte>.Empty, outboundHeaders);
    }

    /// <inheritdoc />
    public async Task<MessageEnvelope> ResolveInboundAsync(
        MessageEnvelope envelope,
        IPayloadStore store,
        CancellationToken ct = default)
    {
        if (envelope is null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        if (store is null)
        {
            throw new ArgumentNullException(nameof(store));
        }

        if (!envelope.Headers.TryGetValue(LargePayloadHeaders.Mode, out var mode) ||
            !string.Equals(mode, LargePayloadHeaders.ModeExternal, StringComparison.OrdinalIgnoreCase))
        {
            return CopyEnvelope(envelope, envelope.Body, envelope.Headers);
        }

        if (!envelope.Headers.TryGetValue(LargePayloadHeaders.Reference, out var reference) ||
            string.IsNullOrWhiteSpace(reference))
        {
            throw new InvalidOperationException("LargePayload: Missing payload reference header.");
        }

        using var payloadStream = await store.DownloadAsync(reference, ct).ConfigureAwait(false);
        if (payloadStream is null)
        {
            throw new InvalidOperationException($"Payload store returned no stream for reference '{reference}'.");
        }

        var storedPayload = await ReadAllBytesAsync(payloadStream, ct).ConfigureAwait(false);
        var resolvedPayload = ResolvePayloadEncoding(storedPayload, envelope.Headers);
        ValidateSha256IfPresent(resolvedPayload, envelope.Headers);

        return CopyEnvelope(envelope, new ReadOnlyMemory<byte>(resolvedPayload), envelope.Headers);
    }

    private static MessageEnvelope CopyEnvelope(
        MessageEnvelope source,
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, string> headers)
    {
        return new MessageEnvelope(
            source.MessageId,
            source.CorrelationId,
            source.SentAtUtc,
            body,
            headers);
    }

    private static Dictionary<string, string> CopyHeaders(IReadOnlyDictionary<string, string> headers)
    {
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in headers)
        {
            copy[pair.Key] = pair.Value;
        }

        return copy;
    }

    private static byte[] ResolvePayloadEncoding(byte[] payload, IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue(LargePayloadHeaders.Encoding, out var encoding) || string.IsNullOrWhiteSpace(encoding))
        {
            return payload;
        }

        if (string.Equals(encoding, LargePayloadHeaders.EncodingGzip, StringComparison.OrdinalIgnoreCase))
        {
            return DecompressGzip(payload);
        }

        throw new InvalidOperationException(
            $"Unsupported payload encoding '{encoding}' for external payload resolution.");
    }

    private static void ValidateSha256IfPresent(byte[] payload, IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue(LargePayloadHeaders.Sha256, out var expectedSha256) ||
            string.IsNullOrWhiteSpace(expectedSha256))
        {
            return;
        }

        var actualSha256 = ComputeSha256Hex(payload);
        if (!string.Equals(expectedSha256, actualSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("LargePayload: Payload hash mismatch.");
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken ct)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (!stream.CanRead)
        {
            throw new InvalidOperationException("Payload stream must be readable.");
        }

        using var output = new MemoryStream();
        await stream.CopyToAsync(output, bufferSize: 81920, ct).ConfigureAwait(false);
        return output.ToArray();
    }

    private static byte[] CompressGzip(byte[] payload)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(payload, 0, payload.Length);
        }

        return output.ToArray();
    }

    private static byte[] DecompressGzip(byte[] payload)
    {
        using var input = new MemoryStream(payload, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: false);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static string ComputeSha256Hex(byte[] payload)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(payload);
        return ToLowerHex(hash);
    }

    private static string ToLowerHex(byte[] bytes)
    {
        var chars = new char[bytes.Length * 2];
        var index = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            var value = bytes[i];
            chars[index++] = ToHexChar(value >> 4);
            chars[index++] = ToHexChar(value & 0xF);
        }

        return new string(chars);
    }

    private static char ToHexChar(int value)
    {
        return (char)(value < 10 ? '0' + value : 'a' + (value - 10));
    }
}
