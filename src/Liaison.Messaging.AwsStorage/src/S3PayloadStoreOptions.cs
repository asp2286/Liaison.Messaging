namespace Liaison.Messaging.AwsStorage;

using System;
using System.Collections.Generic;
using Amazon.S3;
using Amazon.S3.Model;

/// <summary>
/// Options for <see cref="AwsS3PayloadStore"/>.
/// </summary>
public sealed class S3PayloadStoreOptions
{
    /// <summary>
    /// Gets or sets the preconfigured S3 client.
    /// </summary>
    public IAmazonS3? Client { get; set; }

    /// <summary>
    /// Gets or sets the required bucket name.
    /// </summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional static object-key prefix.
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether writes overwrite existing objects.
    /// </summary>
    public bool Overwrite { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether payload expiry is emitted as metadata and tags.
    /// </summary>
    public bool EmitExpiresMarker { get; set; } = true;

    /// <summary>
    /// Gets or sets static metadata to apply to each upload.
    /// </summary>
    public IReadOnlyDictionary<string, string>? StaticMetadata { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this bucket/client configuration supports conditional PUT.
    /// </summary>
    public bool SupportsConditionalPut { get; set; } = true;

    /// <summary>
    /// Gets or sets an optional callback for provider-specific put customization.
    /// </summary>
    public Action<PutObjectRequest>? ConfigurePut { get; set; }
}
