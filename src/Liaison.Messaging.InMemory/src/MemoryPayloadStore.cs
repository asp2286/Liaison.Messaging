namespace Liaison.Messaging.InMemory;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Liaison.Messaging;

/// <summary>
/// Provides a process-local in-memory implementation of <see cref="IPayloadStore"/>.
/// </summary>
public sealed class MemoryPayloadStore : IPayloadStore
{
    private readonly ConcurrentDictionary<string, StoredPayload> _entries = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _utcNow;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryPayloadStore"/> type.
    /// </summary>
    public MemoryPayloadStore()
        : this(static () => DateTimeOffset.UtcNow)
    {
    }

    internal MemoryPayloadStore(Func<DateTimeOffset> utcNow)
    {
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    /// <inheritdoc />
    public async Task<string> UploadAsync(
        Stream payload,
        string keyPrefix,
        long? sizeHintBytes = null,
        DateTimeOffset? expiresAtUtc = null,
        CancellationToken ct = default)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        if (!payload.CanRead)
        {
            throw new InvalidOperationException("Payload stream must be readable.");
        }

        if (string.IsNullOrWhiteSpace(keyPrefix))
        {
            throw new ArgumentException("Key prefix must be provided.", nameof(keyPrefix));
        }

        if (sizeHintBytes.HasValue && sizeHintBytes.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeHintBytes), "Size hint must be greater than or equal to zero.");
        }

        ct.ThrowIfCancellationRequested();

        using var buffer = new MemoryStream();
        await payload.CopyToAsync(buffer, bufferSize: 81920, ct).ConfigureAwait(false);
        var reference = $"{keyPrefix.Trim()}/{Guid.NewGuid():N}";
        _entries[reference] = new StoredPayload(buffer.ToArray(), expiresAtUtc?.ToUniversalTime());
        return reference;
    }

    /// <inheritdoc />
    public Task<Stream> DownloadAsync(string reference, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("Payload reference must be provided.", nameof(reference));
        }

        ct.ThrowIfCancellationRequested();

        if (!_entries.TryGetValue(reference, out var entry))
        {
            throw new InvalidOperationException($"Payload reference '{reference}' was not found.");
        }

        if (entry.ExpiresAtUtc.HasValue && entry.ExpiresAtUtc.Value <= _utcNow())
        {
            _entries.TryRemove(reference, out _);
            throw new InvalidOperationException($"Payload reference '{reference}' has expired.");
        }

        Stream result = new MemoryStream(entry.Bytes, writable: false);
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string reference, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("Payload reference must be provided.", nameof(reference));
        }

        ct.ThrowIfCancellationRequested();
        _entries.TryRemove(reference, out _);
        return Task.CompletedTask;
    }

    private sealed record StoredPayload(byte[] Bytes, DateTimeOffset? ExpiresAtUtc);
}
