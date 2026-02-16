namespace Liaison.Messaging.AzureStorage;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Liaison.Messaging;
using Microsoft.Extensions.Options;

/// <summary>
/// Azure Blob Storage implementation of <see cref="IPayloadStore"/>.
/// </summary>
public sealed class AzureBlobPayloadStore : IPayloadStore
{
    private const string ExpiresMetadataKey = "liaison-expires-at";
    private readonly BlobContainerClient _container;
    private readonly IReadOnlyDictionary<string, string>? _staticMetadata;
    private readonly string? _prefix;
    private readonly bool _overwrite;
    private readonly bool _emitExpiresMarker;
    private readonly Action<BlobUploadOptions>? _configureUpload;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureBlobPayloadStore"/> type.
    /// </summary>
    /// <param name="options">The configured options.</param>
    public AzureBlobPayloadStore(IOptions<AzureBlobPayloadStoreOptions> options)
        : this(options?.Value ?? throw new ArgumentNullException(nameof(options)))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureBlobPayloadStore"/> type.
    /// </summary>
    /// <param name="options">The configured options.</param>
    public AzureBlobPayloadStore(AzureBlobPayloadStoreOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.ContainerName))
        {
            throw new ArgumentException("ContainerName must be provided.", nameof(options));
        }

        if (options.Client is null && string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new ArgumentException("Either Client or ConnectionString must be provided.", nameof(options));
        }

        var client = options.Client ?? new BlobServiceClient(options.ConnectionString);
        _container = client.GetBlobContainerClient(options.ContainerName.Trim());
        _staticMetadata = CopyMetadata(options.StaticMetadata);
        _prefix = NormalizePrefix(options.Prefix);
        _overwrite = options.Overwrite;
        _emitExpiresMarker = options.EmitExpiresMarker;
        _configureUpload = options.ConfigureUpload;
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

        var reference = BuildUploadReference(keyPrefix);
        var blob = _container.GetBlobClient(reference);
        var requiredMetadata = BuildMetadata(expiresAtUtc);
        var uploadOptions = new BlobUploadOptions();

        if (requiredMetadata.Count > 0)
        {
            uploadOptions.Metadata = new Dictionary<string, string>(requiredMetadata, StringComparer.Ordinal);
        }

        _configureUpload?.Invoke(uploadOptions);
        EnforceRequiredMetadata(uploadOptions, requiredMetadata);

        if (!_overwrite)
        {
            uploadOptions.Conditions ??= new BlobRequestConditions();
            uploadOptions.Conditions.IfNoneMatch = ETag.All;
        }

        try
        {
            await blob.UploadAsync(payload, uploadOptions, ct).ConfigureAwait(false);
            return reference;
        }
        catch (OperationCanceledException exception) when (!ct.IsCancellationRequested)
        {
            throw new PayloadStoreUnavailableException(
                $"Azure Blob Storage is unavailable while uploading payload reference '{reference}'.",
                exception);
        }
        catch (IOException exception)
        {
            throw new PayloadStoreUnavailableException(
                $"Azure Blob Storage is unavailable while uploading payload reference '{reference}'.",
                exception);
        }
        catch (RequestFailedException exception)
        {
            throw MapUploadException(reference, exception);
        }
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(string reference, CancellationToken ct = default)
    {
        var normalizedReference = NormalizeReference(reference, nameof(reference));
        ct.ThrowIfCancellationRequested();

        try
        {
            var blob = _container.GetBlobClient(normalizedReference);
            var response = await blob.DownloadStreamingAsync(cancellationToken: ct).ConfigureAwait(false);
            return response.Value.Content;
        }
        catch (OperationCanceledException exception) when (!ct.IsCancellationRequested)
        {
            throw new PayloadStoreUnavailableException(
                $"Azure Blob Storage is unavailable while downloading payload reference '{normalizedReference}'.",
                exception);
        }
        catch (IOException exception)
        {
            throw new PayloadStoreUnavailableException(
                $"Azure Blob Storage is unavailable while downloading payload reference '{normalizedReference}'.",
                exception);
        }
        catch (RequestFailedException exception)
        {
            throw MapDownloadException(normalizedReference, exception);
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string reference, CancellationToken ct = default)
    {
        var normalizedReference = NormalizeReference(reference, nameof(reference));
        ct.ThrowIfCancellationRequested();

        try
        {
            var blob = _container.GetBlobClient(normalizedReference);
            await blob.DeleteIfExistsAsync(
                    DeleteSnapshotsOption.IncludeSnapshots,
                    cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!ct.IsCancellationRequested)
        {
            throw new PayloadStoreUnavailableException(
                $"Azure Blob Storage is unavailable while deleting payload reference '{normalizedReference}'.",
                exception);
        }
        catch (IOException exception)
        {
            throw new PayloadStoreUnavailableException(
                $"Azure Blob Storage is unavailable while deleting payload reference '{normalizedReference}'.",
                exception);
        }
        catch (RequestFailedException exception)
        {
            throw MapDeleteException(normalizedReference, exception);
        }
    }

    private Exception MapUploadException(string reference, RequestFailedException exception)
    {
        if (IsAccessDenied(exception.Status))
        {
            return new PayloadAccessDeniedException(
                reference,
                $"Access denied while uploading payload reference '{reference}'.",
                exception);
        }

        if (!_overwrite && (exception.Status == 409 || exception.Status == 412))
        {
            return new PayloadAlreadyExistsException(
                reference,
                $"Payload reference '{reference}' already exists.",
                exception);
        }

        if (exception.Status == 404)
        {
            return new PayloadStoreUnavailableException(
                $"Blob container '{_container.Name}' was not found while uploading payload reference '{reference}'.",
                exception);
        }

        if (IsTransient(exception.Status))
        {
            return new PayloadStoreUnavailableException(
                $"Azure Blob Storage is unavailable while uploading payload reference '{reference}'.",
                exception);
        }

        return new InvalidOperationException(
            $"Azure Blob upload failed for payload reference '{reference}'.",
            exception);
    }

    private Exception MapDownloadException(string reference, RequestFailedException exception)
    {
        if (exception.Status == 404)
        {
            return new PayloadNotFoundException(reference, exception);
        }

        if (IsAccessDenied(exception.Status))
        {
            return new PayloadAccessDeniedException(reference, exception);
        }

        if (IsTransient(exception.Status))
        {
            return new PayloadStoreUnavailableException(
                $"Azure Blob Storage is unavailable while downloading payload reference '{reference}'.",
                exception);
        }

        return new InvalidOperationException(
            $"Azure Blob download failed for payload reference '{reference}'.",
            exception);
    }

    private Exception MapDeleteException(string reference, RequestFailedException exception)
    {
        if (IsAccessDenied(exception.Status))
        {
            return new PayloadAccessDeniedException(
                reference,
                $"Access denied while deleting payload reference '{reference}'.",
                exception);
        }

        if (exception.Status == 404)
        {
            return new PayloadStoreUnavailableException(
                $"Blob container '{_container.Name}' was not found while deleting payload reference '{reference}'.",
                exception);
        }

        if (IsTransient(exception.Status))
        {
            return new PayloadStoreUnavailableException(
                $"Azure Blob Storage is unavailable while deleting payload reference '{reference}'.",
                exception);
        }

        return new InvalidOperationException(
            $"Azure Blob delete failed for payload reference '{reference}'.",
            exception);
    }

    private Dictionary<string, string> BuildMetadata(DateTimeOffset? expiresAtUtc)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_staticMetadata is not null)
        {
            foreach (var pair in _staticMetadata)
            {
                metadata[pair.Key] = pair.Value;
            }
        }

        if (_emitExpiresMarker && expiresAtUtc.HasValue)
        {
            metadata[ExpiresMetadataKey] = expiresAtUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }

        return metadata;
    }

    private static void EnforceRequiredMetadata(
        BlobUploadOptions uploadOptions,
        IReadOnlyDictionary<string, string> requiredMetadata)
    {
        if (uploadOptions.Metadata is null)
        {
            uploadOptions.Metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        foreach (var pair in requiredMetadata)
        {
            uploadOptions.Metadata[pair.Key] = pair.Value;
        }
    }

    private static bool IsAccessDenied(int status)
    {
        return status == 401 || status == 403;
    }

    private static bool IsTransient(int status)
    {
        return status == 408 || status == 429 || status == 500 || status == 502 || status == 503 || status == 504;
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
}
