namespace Liaison.Messaging.AwsStorage;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Liaison.Messaging;
using Microsoft.Extensions.Options;

/// <summary>
/// Amazon S3 implementation of <see cref="IPayloadStore"/>.
/// </summary>
public sealed class AwsS3PayloadStore : IPayloadStore
{
    private const string ExpiresMarkerKey = "liaison-expires-at";
    private const string DefaultContentType = "application/octet-stream";
    private readonly IAmazonS3 _client;
    private readonly string _bucketName;
    private readonly string? _prefix;
    private readonly bool _overwrite;
    private readonly bool _emitExpiresMarker;
    private readonly bool _supportsConditionalPut;
    private readonly IReadOnlyDictionary<string, string>? _staticMetadata;
    private readonly Action<PutObjectRequest>? _configurePut;

    /// <summary>
    /// Initializes a new instance of the <see cref="AwsS3PayloadStore"/> type.
    /// </summary>
    /// <param name="options">The configured options.</param>
    public AwsS3PayloadStore(IOptions<S3PayloadStoreOptions> options)
        : this(options?.Value ?? throw new ArgumentNullException(nameof(options)))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AwsS3PayloadStore"/> type.
    /// </summary>
    /// <param name="options">The configured options.</param>
    public AwsS3PayloadStore(S3PayloadStoreOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (options.Client is null)
        {
            throw new ArgumentException("Client must be provided.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.BucketName))
        {
            throw new ArgumentException("BucketName must be provided.", nameof(options));
        }

        _client = options.Client;
        _bucketName = options.BucketName.Trim();
        _prefix = NormalizePrefix(options.Prefix);
        _overwrite = options.Overwrite;
        _emitExpiresMarker = options.EmitExpiresMarker;
        _supportsConditionalPut = options.SupportsConditionalPut;
        _staticMetadata = CopyMetadata(options.StaticMetadata);
        _configurePut = options.ConfigurePut;
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

        if (sizeHintBytes.HasValue && sizeHintBytes.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeHintBytes), "Size hint must be greater than or equal to zero.");
        }

        ct.ThrowIfCancellationRequested();

        if (!_overwrite && !_supportsConditionalPut)
        {
            throw new NotSupportedException(
                "Conditional PUT is required when Overwrite is false, but this store configuration has SupportsConditionalPut=false.");
        }

        var reference = BuildUploadReference(keyPrefix);
        var expiresMarker = _emitExpiresMarker && expiresAtUtc.HasValue
            ? expiresAtUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            : null;
        var metadata = BuildMetadata(expiresMarker);
        var resolvedContentLength = ResolveContentLength(payload, sizeHintBytes);

        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = reference,
            InputStream = payload,
            AutoCloseStream = false,
            ContentType = DefaultContentType,
        };

        SetContentLengthIfPresent(request, resolvedContentLength);
        EnforceRequiredMetadataAndTags(request, metadata, expiresMarker);

        _configurePut?.Invoke(request);

        EnforceRequestInvariants(
            request,
            reference,
            payload,
            resolvedContentLength,
            metadata,
            expiresMarker);

        if (!_overwrite)
        {
            request.IfNoneMatch = "*";
        }

        try
        {
            await _client.PutObjectAsync(request, ct).ConfigureAwait(false);
            return reference;
        }
        catch (OperationCanceledException exception) when (!ct.IsCancellationRequested)
        {
            throw new PayloadStoreUnavailableException(
                $"S3 is unavailable while uploading payload reference '{reference}'.",
                exception);
        }
        catch (AmazonS3Exception exception)
        {
            throw MapUploadException(reference, exception);
        }
        catch (IOException exception)
        {
            throw new PayloadStoreUnavailableException(
                $"S3 is unavailable while uploading payload reference '{reference}'.",
                exception);
        }
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(string reference, CancellationToken ct = default)
    {
        var normalizedReference = NormalizeReference(reference, nameof(reference));
        ct.ThrowIfCancellationRequested();

        try
        {
            var response = await _client.GetObjectAsync(_bucketName, normalizedReference, ct).ConfigureAwait(false);
            return new S3ResponseStream(response);
        }
        catch (OperationCanceledException exception) when (!ct.IsCancellationRequested)
        {
            throw new PayloadStoreUnavailableException(
                $"S3 is unavailable while downloading payload reference '{normalizedReference}'.",
                exception);
        }
        catch (AmazonS3Exception exception)
        {
            throw MapDownloadException(normalizedReference, exception);
        }
        catch (IOException exception)
        {
            throw new PayloadStoreUnavailableException(
                $"S3 is unavailable while downloading payload reference '{normalizedReference}'.",
                exception);
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string reference, CancellationToken ct = default)
    {
        var normalizedReference = NormalizeReference(reference, nameof(reference));
        ct.ThrowIfCancellationRequested();

        try
        {
            var request = new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = normalizedReference,
            };

            await _client.DeleteObjectAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!ct.IsCancellationRequested)
        {
            throw new PayloadStoreUnavailableException(
                $"S3 is unavailable while deleting payload reference '{normalizedReference}'.",
                exception);
        }
        catch (AmazonS3Exception exception)
        {
            throw MapDeleteException(normalizedReference, exception);
        }
        catch (IOException exception)
        {
            throw new PayloadStoreUnavailableException(
                $"S3 is unavailable while deleting payload reference '{normalizedReference}'.",
                exception);
        }
    }

    private Exception MapUploadException(string reference, AmazonS3Exception exception)
    {
        if (IsAccessDenied(exception))
        {
            return new PayloadAccessDeniedException(
                reference,
                $"Access denied while uploading payload reference '{reference}'.",
                exception);
        }

        if (!_overwrite && _supportsConditionalPut && IsPreconditionFailed(exception))
        {
            return new PayloadAlreadyExistsException(reference, exception);
        }

        if (!_overwrite && _supportsConditionalPut && IsConditionalConflict(exception))
        {
            return new PayloadStoreConditionalConflictException(
                reference,
                exception);
        }

        if (IsTransient(exception.StatusCode))
        {
            return new PayloadStoreUnavailableException(
                $"S3 is unavailable while uploading payload reference '{reference}'.",
                exception);
        }

        if (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return new PayloadStoreUnavailableException(
                $"S3 bucket '{_bucketName}' was not found while uploading payload reference '{reference}'.",
                exception);
        }

        return new InvalidOperationException(
            $"S3 upload failed for payload reference '{reference}'.",
            exception);
    }

    private Exception MapDownloadException(string reference, AmazonS3Exception exception)
    {
        if (IsNotFound(exception))
        {
            return new PayloadNotFoundException(reference, exception);
        }

        if (IsAccessDenied(exception))
        {
            return new PayloadAccessDeniedException(reference, exception);
        }

        if (IsTransient(exception.StatusCode))
        {
            return new PayloadStoreUnavailableException(
                $"S3 is unavailable while downloading payload reference '{reference}'.",
                exception);
        }

        return new InvalidOperationException(
            $"S3 download failed for payload reference '{reference}'.",
            exception);
    }

    private Exception MapDeleteException(string reference, AmazonS3Exception exception)
    {
        if (IsAccessDenied(exception))
        {
            return new PayloadAccessDeniedException(
                reference,
                $"Access denied while deleting payload reference '{reference}'.",
                exception);
        }

        if (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return new PayloadStoreUnavailableException(
                $"S3 bucket '{_bucketName}' was not found while deleting payload reference '{reference}'.",
                exception);
        }

        if (IsTransient(exception.StatusCode))
        {
            return new PayloadStoreUnavailableException(
                $"S3 is unavailable while deleting payload reference '{reference}'.",
                exception);
        }

        return new InvalidOperationException(
            $"S3 delete failed for payload reference '{reference}'.",
            exception);
    }

    private static bool IsNotFound(AmazonS3Exception exception)
    {
        return exception.StatusCode == HttpStatusCode.NotFound ||
               string.Equals(exception.ErrorCode, "NoSuchKey", StringComparison.Ordinal) ||
               string.Equals(exception.ErrorCode, "NotFound", StringComparison.Ordinal);
    }

    private static bool IsAccessDenied(AmazonS3Exception exception)
    {
        return exception.StatusCode == HttpStatusCode.Forbidden ||
               exception.StatusCode == HttpStatusCode.Unauthorized ||
               string.Equals(exception.ErrorCode, "AccessDenied", StringComparison.Ordinal);
    }

    private static bool IsPreconditionFailed(AmazonS3Exception exception)
    {
        return exception.StatusCode == HttpStatusCode.PreconditionFailed ||
               string.Equals(exception.ErrorCode, "PreconditionFailed", StringComparison.Ordinal);
    }

    private static bool IsConditionalConflict(AmazonS3Exception exception)
    {
        return exception.StatusCode == HttpStatusCode.Conflict ||
               string.Equals(exception.ErrorCode, "ConditionalRequestConflict", StringComparison.Ordinal);
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.RequestTimeout ||
               (int)statusCode == 429 ||
               statusCode == HttpStatusCode.InternalServerError ||
               statusCode == HttpStatusCode.BadGateway ||
               statusCode == HttpStatusCode.ServiceUnavailable ||
               statusCode == HttpStatusCode.GatewayTimeout;
    }

    private string BuildUploadReference(string keyPrefix)
    {
        if (keyPrefix is null)
        {
            throw new ArgumentNullException(nameof(keyPrefix));
        }

        var normalizedPrefix = NormalizeReference(keyPrefix, nameof(keyPrefix));
        return CombinePrefix(_prefix, normalizedPrefix);
    }

    private static string NormalizeReference(string reference, string paramName)
    {
        if (reference is null)
        {
            throw new ArgumentNullException(paramName);
        }

        var trimmed = reference.Trim();
        if (trimmed.Length == 0)
        {
            throw new PayloadReferenceInvalidException("Payload reference must be provided.");
        }

        var normalized = trimmed.Trim('/');
        if (normalized.Length == 0)
        {
            throw new PayloadReferenceInvalidException("Payload reference must contain non-separator characters.");
        }

        return normalized;
    }

    private static string? NormalizePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return null;
        }

        return prefix.Trim().Trim('/');
    }

    private static IReadOnlyDictionary<string, string>? CopyMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in metadata)
        {
            copy[pair.Key] = pair.Value;
        }

        return copy;
    }

    private static string CombinePrefix(string? prefix, string reference)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return reference;
        }

        return string.Concat(prefix, "/", reference);
    }

    private Dictionary<string, string> BuildMetadata(string? expiresMarker)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_staticMetadata is not null)
        {
            foreach (var pair in _staticMetadata)
            {
                metadata[pair.Key] = pair.Value;
            }
        }

        if (expiresMarker is not null)
        {
            metadata[ExpiresMarkerKey] = expiresMarker;
        }

        return metadata;
    }

    private static long? ResolveContentLength(Stream payload, long? sizeHintBytes)
    {
        if (sizeHintBytes.HasValue)
        {
            return sizeHintBytes.Value;
        }

        if (!payload.CanSeek)
        {
            return null;
        }

        var remaining = payload.Length - payload.Position;
        if (remaining < 0)
        {
            return null;
        }

        return remaining;
    }

    private static void SetContentLengthIfPresent(PutObjectRequest request, long? contentLength)
    {
        if (contentLength.HasValue)
        {
            request.Headers.ContentLength = contentLength.Value;
        }
    }

    private void EnforceRequestInvariants(
        PutObjectRequest request,
        string reference,
        Stream payload,
        long? contentLength,
        IReadOnlyDictionary<string, string> requiredMetadata,
        string? expiresMarker)
    {
        request.BucketName = _bucketName;
        request.Key = reference;
        request.InputStream = payload;
        request.AutoCloseStream = false;
        request.ContentType = DefaultContentType;
        SetContentLengthIfPresent(request, contentLength);
        EnforceRequiredMetadataAndTags(request, requiredMetadata, expiresMarker);
    }

    private static void EnforceRequiredMetadataAndTags(
        PutObjectRequest request,
        IReadOnlyDictionary<string, string> requiredMetadata,
        string? expiresMarker)
    {
        foreach (var pair in requiredMetadata)
        {
            request.Metadata[pair.Key] = pair.Value;
        }

        if (expiresMarker is not null)
        {
            UpsertTag(request, ExpiresMarkerKey, expiresMarker);
        }
    }

    private static void UpsertTag(PutObjectRequest request, string key, string value)
    {
        request.TagSet ??= new List<Tag>();
        for (var i = 0; i < request.TagSet.Count; i++)
        {
            if (string.Equals(request.TagSet[i].Key, key, StringComparison.Ordinal))
            {
                request.TagSet[i] = new Tag { Key = key, Value = value };
                return;
            }
        }

        request.TagSet.Add(new Tag { Key = key, Value = value });
    }

    private sealed class S3ResponseStream : Stream
    {
        private readonly GetObjectResponse _response;
        private readonly Stream _inner;

        public S3ResponseStream(GetObjectResponse response)
        {
            _response = response ?? throw new ArgumentNullException(nameof(response));
            _inner = _response.ResponseStream;
        }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => _inner.CanWrite;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush()
        {
            _inner.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return _inner.FlushAsync(cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            return _inner.Read(buffer);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _inner.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return _inner.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _inner.Write(buffer);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return _inner.WriteAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _response.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
            _response.Dispose();
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
