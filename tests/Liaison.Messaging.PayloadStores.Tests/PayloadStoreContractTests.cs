namespace Liaison.Messaging.PayloadStores.Tests;

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Liaison.Messaging;
using Xunit;

public abstract class PayloadStoreContractTests
{
    private readonly PayloadStoreFixture _fixture;

    protected PayloadStoreContractTests(PayloadStoreFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PutThenGet_Roundtrip()
    {
        if (!_fixture.IsEnabled)
        {
            return;
        }

        var store = _fixture.CreateStore(overwrite: true);
        var payload = Encoding.UTF8.GetBytes("payload-roundtrip");
        await using var uploadStream = new MemoryStream(payload, writable: false);

        var reference = await store.UploadAsync(uploadStream, _fixture.CreateReferencePrefix());
        await using var downloaded = await store.DownloadAsync(reference);
        using var copy = new MemoryStream();
        await downloaded.CopyToAsync(copy);

        Assert.Equal(payload, copy.ToArray());

        await store.DeleteAsync(reference);
    }

    [Fact]
    public async Task Get_Missing_ThrowsPayloadNotFound()
    {
        if (!_fixture.IsEnabled)
        {
            return;
        }

        var store = _fixture.CreateStore(overwrite: true);
        var payload = Encoding.UTF8.GetBytes("payload-existing");
        await using var uploadStream = new MemoryStream(payload, writable: false);
        var existingReference = await store.UploadAsync(uploadStream, _fixture.CreateReferencePrefix());

        await store.DeleteAsync(existingReference);

        var missingReference = string.Concat(existingReference, "-missing");
        var exception = await Assert.ThrowsAsync<PayloadNotFoundException>(
            () => store.DownloadAsync(missingReference));

        Assert.Equal(missingReference, exception.Reference);
    }

    [Fact]
    public async Task Put_WhenExists_OverwriteFalse_ThrowsPayloadAlreadyExists()
    {
        if (!_fixture.IsEnabled)
        {
            return;
        }

        if (_fixture.SupportsConditionalPut)
        {
            var store = _fixture.CreateStore(overwrite: false, supportsConditionalPut: true);
            var key = _fixture.CreateReferencePrefix();
            await using var firstUpload = new MemoryStream(Encoding.UTF8.GetBytes("first"), writable: false);
            await using var secondUpload = new MemoryStream(Encoding.UTF8.GetBytes("second"), writable: false);

            var reference = await store.UploadAsync(firstUpload, key);

            var exception = await Assert.ThrowsAsync<PayloadAlreadyExistsException>(
                () => store.UploadAsync(secondUpload, key));

            Assert.Equal(reference, exception.Reference);
            await store.DeleteAsync(reference);
            return;
        }

        if (_fixture.CanDisableConditionalPut)
        {
            var store = _fixture.CreateStore(overwrite: false, supportsConditionalPut: false);
            await using var uploadStream = new MemoryStream(Encoding.UTF8.GetBytes("payload"), writable: false);
            await Assert.ThrowsAsync<NotSupportedException>(
                () => store.UploadAsync(uploadStream, _fixture.CreateReferencePrefix()));
        }
    }

    [Fact]
    public async Task Put_WhenConditionalPutUnsupported_ThrowsNotSupported()
    {
        if (!_fixture.IsEnabled || !_fixture.CanDisableConditionalPut)
        {
            return;
        }

        var store = _fixture.CreateStore(overwrite: false, supportsConditionalPut: false);
        await using var uploadStream = new MemoryStream(Encoding.UTF8.GetBytes("payload"), writable: false);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => store.UploadAsync(uploadStream, _fixture.CreateReferencePrefix()));
    }

    [Fact]
    public async Task Put_WritesExpiresMarker_WhenProvided()
    {
        if (!_fixture.IsEnabled)
        {
            return;
        }

        var store = _fixture.CreateStore(overwrite: true, emitExpiresMarker: true);
        var expiresAtUtc = new DateTimeOffset(2026, 2, 13, 20, 0, 0, TimeSpan.Zero);
        await using var uploadStream = new MemoryStream(Encoding.UTF8.GetBytes("payload-expires"), writable: false);

        var reference = await store.UploadAsync(
            uploadStream,
            _fixture.CreateReferencePrefix(),
            expiresAtUtc: expiresAtUtc);

        if (_fixture.CanVerifyExpiresMarker)
        {
            var marker = await _fixture.ReadExpiresMarkerAsync(reference);
            Assert.Equal(expiresAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), marker);
        }

        await store.DeleteAsync(reference);
    }
}
