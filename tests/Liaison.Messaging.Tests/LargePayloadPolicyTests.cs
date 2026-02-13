namespace Liaison.Messaging.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Liaison.Messaging;
using Xunit;

public sealed class LargePayloadPolicyTests
{
    [Fact]
    public async Task PrepareOutboundAsync_ReturnsUnchangedEnvelopeWhenBodyIsUnderThreshold()
    {
        var policy = new DefaultLargePayloadPolicy(new LargePayloadPolicyOptions(ThresholdBytes: 32));
        var store = new CapturingPayloadStore("ref-1");
        var envelope = CreateEnvelope(Encoding.UTF8.GetBytes("small"), new Dictionary<string, string> { ["x"] = "1" });

        var prepared = await policy.PrepareOutboundAsync(envelope, store);

        Assert.NotSame(envelope, prepared);
        Assert.Equal(envelope.Body.ToArray(), prepared.Body.ToArray());
        Assert.Equal("1", prepared.Headers["x"]);
        Assert.False(prepared.Headers.ContainsKey(LargePayloadHeaders.Mode));
        Assert.Equal(0, store.UploadCount);
    }

    [Fact]
    public async Task PrepareOutboundAsync_ExternalizesPayloadAndAddsHeaders()
    {
        var options = new LargePayloadPolicyOptions(ThresholdBytes: 4, UseCompression: false, KeyPrefix: "payload");
        var policy = new DefaultLargePayloadPolicy(options);
        var store = new CapturingPayloadStore("ref-abc");
        var payload = Encoding.UTF8.GetBytes("this-is-large");
        var envelope = CreateEnvelope(payload, new Dictionary<string, string> { ["user"] = "header" });
        var expiresAt = new DateTimeOffset(2026, 2, 13, 18, 0, 0, TimeSpan.Zero);

        var prepared = await policy.PrepareOutboundAsync(envelope, store, expiresAt);

        Assert.Empty(prepared.Body.ToArray());
        Assert.Equal(LargePayloadHeaders.ModeExternal, prepared.Headers[LargePayloadHeaders.Mode]);
        Assert.Equal("ref-abc", prepared.Headers[LargePayloadHeaders.Reference]);
        Assert.Equal(payload.LongLength.ToString(), prepared.Headers[LargePayloadHeaders.Size]);
        Assert.Equal(ComputeSha256Hex(payload), prepared.Headers[LargePayloadHeaders.Sha256]);
        Assert.Equal(expiresAt.ToUniversalTime().ToString("O"), prepared.Headers[LargePayloadHeaders.Expires]);
        Assert.False(prepared.Headers.ContainsKey(LargePayloadHeaders.Encoding));
        Assert.Equal(payload, store.LastUploadedPayload);
        Assert.Equal("payload/id-1", store.LastKeyPrefix);
        Assert.Equal(payload.LongLength, store.LastSizeHintBytes);
        Assert.Equal(expiresAt, store.LastExpiresAtUtc);
        Assert.False(envelope.Headers.ContainsKey(LargePayloadHeaders.Mode));
    }

    [Fact]
    public async Task PrepareOutboundAsync_SetsGzipEncodingWhenCompressionEnabled()
    {
        var policy = new DefaultLargePayloadPolicy(new LargePayloadPolicyOptions(ThresholdBytes: 4, UseCompression: true));
        var store = new CapturingPayloadStore("ref-gzip");
        var payload = Encoding.UTF8.GetBytes(new string('a', 128));
        var envelope = CreateEnvelope(payload);

        var prepared = await policy.PrepareOutboundAsync(envelope, store);

        Assert.Equal(LargePayloadHeaders.EncodingGzip, prepared.Headers[LargePayloadHeaders.Encoding]);
        Assert.NotEqual(payload, store.LastUploadedPayload);
        Assert.Equal(payload, Decompress(store.LastUploadedPayload));
    }

    [Fact]
    public async Task ResolveInboundAsync_ThrowsWhenReferenceHeaderIsMissing()
    {
        var policy = new DefaultLargePayloadPolicy(new LargePayloadPolicyOptions(ThresholdBytes: 1));
        var store = new CapturingPayloadStore("unused");
        var envelope = CreateEnvelope(
            body: Array.Empty<byte>(),
            headers: new Dictionary<string, string> { [LargePayloadHeaders.Mode] = LargePayloadHeaders.ModeExternal });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => policy.ResolveInboundAsync(envelope, store));

        Assert.Equal("LargePayload: Missing payload reference header.", exception.Message);
    }

    [Fact]
    public async Task ResolveInboundAsync_RestoresExternalizedPayloadAndValidatesHash()
    {
        var policy = new DefaultLargePayloadPolicy(new LargePayloadPolicyOptions(ThresholdBytes: 4, UseCompression: true));
        var store = new CapturingPayloadStore("ref-roundtrip");
        var payload = Encoding.UTF8.GetBytes("payload-roundtrip");
        var envelope = CreateEnvelope(payload, new Dictionary<string, string> { ["trace"] = "123" });

        var prepared = await policy.PrepareOutboundAsync(envelope, store);
        var restored = await policy.ResolveInboundAsync(prepared, store);

        Assert.NotSame(prepared, restored);
        Assert.Equal(payload, restored.Body.ToArray());
        Assert.Equal(LargePayloadHeaders.ModeExternal, restored.Headers[LargePayloadHeaders.Mode]);
        Assert.Equal("123", restored.Headers["trace"]);
    }

    [Fact]
    public async Task ResolveInboundAsync_ThrowsWhenShaDoesNotMatch()
    {
        var policy = new DefaultLargePayloadPolicy(new LargePayloadPolicyOptions(ThresholdBytes: 1));
        var store = new CapturingPayloadStore("ref-sha");
        store.AddPayload("ref-sha", Encoding.UTF8.GetBytes("payload"));
        var envelope = CreateEnvelope(
            body: Array.Empty<byte>(),
            headers: new Dictionary<string, string>
            {
                [LargePayloadHeaders.Mode] = LargePayloadHeaders.ModeExternal,
                [LargePayloadHeaders.Reference] = "ref-sha",
                [LargePayloadHeaders.Sha256] = "bad-hash",
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => policy.ResolveInboundAsync(envelope, store));

        Assert.Equal("LargePayload: Payload hash mismatch.", exception.Message);
    }

    [Fact]
    public async Task ResolveInboundAsync_ThrowsWhenEncodingIsUnsupported()
    {
        var policy = new DefaultLargePayloadPolicy(new LargePayloadPolicyOptions(ThresholdBytes: 1));
        var store = new CapturingPayloadStore("ref-enc");
        store.AddPayload("ref-enc", Encoding.UTF8.GetBytes("payload"));
        var envelope = CreateEnvelope(
            body: Array.Empty<byte>(),
            headers: new Dictionary<string, string>
            {
                [LargePayloadHeaders.Mode] = LargePayloadHeaders.ModeExternal,
                [LargePayloadHeaders.Reference] = "ref-enc",
                [LargePayloadHeaders.Encoding] = "brotli",
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => policy.ResolveInboundAsync(envelope, store));

        Assert.Contains("Unsupported payload encoding", exception.Message, StringComparison.Ordinal);
    }

    private static MessageEnvelope CreateEnvelope(
        byte[] body,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        return new MessageEnvelope(
            messageId: "id-1",
            correlationId: "corr-1",
            sentAtUtc: new DateTimeOffset(2026, 2, 13, 17, 0, 0, TimeSpan.Zero),
            body: new ReadOnlyMemory<byte>(body),
            headers: headers ?? new Dictionary<string, string>());
    }

    private static byte[] Decompress(byte[] payload)
    {
        using var input = new MemoryStream(payload, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static string ComputeSha256Hex(byte[] payload)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(payload);
        var chars = new char[hash.Length * 2];
        var index = 0;
        for (var i = 0; i < hash.Length; i++)
        {
            var value = hash[i];
            chars[index++] = ToHexChar(value >> 4);
            chars[index++] = ToHexChar(value & 0xF);
        }

        return new string(chars);
    }

    private static char ToHexChar(int value)
    {
        return (char)(value < 10 ? '0' + value : 'a' + (value - 10));
    }

    private sealed class CapturingPayloadStore : IPayloadStore
    {
        private readonly string _referenceToReturn;
        private readonly Dictionary<string, byte[]> _entries = new(StringComparer.Ordinal);

        public CapturingPayloadStore(string referenceToReturn)
        {
            _referenceToReturn = referenceToReturn;
        }

        public int UploadCount { get; private set; }

        public string? LastKeyPrefix { get; private set; }

        public long? LastSizeHintBytes { get; private set; }

        public DateTimeOffset? LastExpiresAtUtc { get; private set; }

        public byte[] LastUploadedPayload { get; private set; } = Array.Empty<byte>();

        public async Task<string> UploadAsync(
            Stream payload,
            string keyPrefix,
            long? sizeHintBytes = null,
            DateTimeOffset? expiresAtUtc = null,
            CancellationToken ct = default)
        {
            UploadCount++;
            LastKeyPrefix = keyPrefix;
            LastSizeHintBytes = sizeHintBytes;
            LastExpiresAtUtc = expiresAtUtc;

            using var buffer = new MemoryStream();
            await payload.CopyToAsync(buffer, 81920, ct).ConfigureAwait(false);
            LastUploadedPayload = buffer.ToArray();
            _entries[_referenceToReturn] = LastUploadedPayload;

            return _referenceToReturn;
        }

        public Task<Stream> DownloadAsync(string reference, CancellationToken ct = default)
        {
            if (!_entries.TryGetValue(reference, out var bytes))
            {
                throw new InvalidOperationException($"Payload reference '{reference}' was not found.");
            }

            Stream stream = new MemoryStream(bytes, writable: false);
            return Task.FromResult(stream);
        }

        public Task DeleteAsync(string reference, CancellationToken ct = default)
        {
            _entries.Remove(reference);
            return Task.CompletedTask;
        }

        public void AddPayload(string reference, byte[] payload)
        {
            _entries[reference] = payload;
        }
    }
}
