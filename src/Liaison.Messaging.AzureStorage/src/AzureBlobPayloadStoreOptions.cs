namespace Liaison.Messaging.AzureStorage;

using System;
using System.Collections.Generic;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

/// <summary>
/// Options for <see cref="AzureBlobPayloadStore"/>.
/// </summary>
public sealed class AzureBlobPayloadStoreOptions
{
    /// <summary>
    /// Gets or sets the preconfigured blob service client.
    /// </summary>
    public BlobServiceClient? Client { get; set; }

    /// <summary>
    /// Gets or sets the optional connection string when <see cref="Client"/> is not supplied.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the required container name.
    /// </summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional static blob-name prefix.
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether writes overwrite existing blobs.
    /// </summary>
    public bool Overwrite { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether payload expiry is emitted as metadata.
    /// </summary>
    public bool EmitExpiresMarker { get; set; } = true;

    /// <summary>
    /// Gets or sets static metadata to apply to each upload.
    /// </summary>
    public IReadOnlyDictionary<string, string>? StaticMetadata { get; set; }

    /// <summary>
    /// Gets or sets an optional callback for provider-specific upload customization.
    /// </summary>
    public Action<BlobUploadOptions>? ConfigureUpload { get; set; }
}
