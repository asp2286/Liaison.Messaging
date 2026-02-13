namespace Liaison.Messaging.Tests;

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Liaison.Messaging.InMemory;
using Xunit;

public sealed class MemoryPayloadStoreTests
{
    [Fact]
    public async Task UploadDownloadDeleteAsync_RoundTripsPayload()
    {
        var store = new MemoryPayloadStore();
        var payloadBytes = Encoding.UTF8.GetBytes("payload-content");
        await using var uploadStream = new MemoryStream(payloadBytes, writable: false);

        var reference = await store.UploadAsync(uploadStream, keyPrefix: "payload");
        await using var downloaded = await store.DownloadAsync(reference);
        using var copy = new MemoryStream();
        await downloaded.CopyToAsync(copy);

        Assert.Equal(payloadBytes, copy.ToArray());

        await store.DeleteAsync(reference);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => store.DownloadAsync(reference));
        Assert.Contains("not found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DownloadAsync_ThrowsWhenReferenceIsExpired()
    {
        var store = new MemoryPayloadStore();
        await using var uploadStream = new MemoryStream(Encoding.UTF8.GetBytes("payload"), writable: false);

        var reference = await store.UploadAsync(uploadStream, "payload", expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => store.DownloadAsync(reference));

        Assert.Contains("expired", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadAsync_ThrowsWhenKeyPrefixIsInvalid()
    {
        var store = new MemoryPayloadStore();
        await using var uploadStream = new MemoryStream(Encoding.UTF8.GetBytes("payload"), writable: false);

        await Assert.ThrowsAsync<ArgumentException>(() => store.UploadAsync(uploadStream, keyPrefix: " "));
    }
}
