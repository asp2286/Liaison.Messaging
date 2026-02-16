namespace Liaison.Messaging.PayloadStores.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Liaison.Messaging;
using Liaison.Messaging.AwsStorage;

public sealed class S3PayloadStoreFixture : PayloadStoreFixture
{
    private const string ExpiresMetadataKey = "liaison-expires-at";

    private readonly string? _bucketName;
    private readonly string _prefix;
    private readonly bool _supportsConditionalPut;
    private readonly IAmazonS3? _client;

    public S3PayloadStoreFixture()
    {
        _bucketName = Environment.GetEnvironmentVariable("LIAISON_TEST_S3_BUCKET");
        _prefix = Environment.GetEnvironmentVariable("LIAISON_TEST_S3_PREFIX") ?? "contract-tests";
        _supportsConditionalPut = GetBooleanEnvironmentVariable("LIAISON_TEST_S3_SUPPORTS_CONDITIONAL_PUT", defaultValue: true);

        IsEnabled = !string.IsNullOrWhiteSpace(_bucketName);
        if (IsEnabled)
        {
            _client = CreateClient();
        }
    }

    public override string ProviderName => "S3";

    public override bool IsEnabled { get; }

    public override bool SupportsConditionalPut => _supportsConditionalPut;

    public override bool CanDisableConditionalPut => true;

    public override bool CanVerifyExpiresMarker => IsEnabled;

    public override IPayloadStore CreateStore(
        bool overwrite = false,
        bool emitExpiresMarker = true,
        bool supportsConditionalPut = true)
    {
        if (!IsEnabled || _client is null)
        {
            throw new InvalidOperationException("S3 payload-store fixture is not configured.");
        }

        return new AwsS3PayloadStore(new S3PayloadStoreOptions
        {
            Client = _client,
            BucketName = _bucketName!,
            Prefix = _prefix,
            Overwrite = overwrite,
            EmitExpiresMarker = emitExpiresMarker,
            SupportsConditionalPut = supportsConditionalPut && _supportsConditionalPut,
        });
    }

    public override string CreateReferencePrefix()
    {
        return $"contract/{Guid.NewGuid():N}";
    }

    public override async Task<string?> ReadExpiresMarkerAsync(string reference, CancellationToken ct = default)
    {
        if (!IsEnabled || _client is null)
        {
            return null;
        }

        var metadataResponse = await _client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest
                {
                    BucketName = _bucketName,
                    Key = reference,
                },
                ct)
            .ConfigureAwait(false);

        var metadataMarker = GetMetadataValueCaseInsensitive(metadataResponse.Metadata, ExpiresMetadataKey);
        if (!string.IsNullOrWhiteSpace(metadataMarker))
        {
            return metadataMarker;
        }

        var tagsResponse = await _client.GetObjectTaggingAsync(
                new GetObjectTaggingRequest
                {
                    BucketName = _bucketName,
                    Key = reference,
                },
                ct)
            .ConfigureAwait(false);

        var tag = tagsResponse.Tagging.FirstOrDefault(pair =>
            string.Equals(pair.Key, ExpiresMetadataKey, StringComparison.OrdinalIgnoreCase));

        return tag?.Value;
    }

    public override Task DisposeAsync()
    {
        _client?.Dispose();
        return Task.CompletedTask;
    }

    private static bool GetBooleanEnvironmentVariable(string name, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return string.Equals(value, "1", StringComparison.Ordinal) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetMetadataValueCaseInsensitive(MetadataCollection metadata, string key)
    {
        foreach (var metadataKey in metadata.Keys)
        {
            if (string.Equals(metadataKey, key, StringComparison.OrdinalIgnoreCase))
            {
                var value = metadata[metadataKey];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static IAmazonS3 CreateClient()
    {
        var serviceUrl = Environment.GetEnvironmentVariable("LIAISON_TEST_S3_SERVICE_URL");
        var region = Environment.GetEnvironmentVariable("LIAISON_TEST_S3_REGION") ?? "us-east-1";

        if (!string.IsNullOrWhiteSpace(serviceUrl))
        {
            var accessKey = Environment.GetEnvironmentVariable("LIAISON_TEST_S3_ACCESS_KEY_ID") ?? "test";
            var secretKey = Environment.GetEnvironmentVariable("LIAISON_TEST_S3_SECRET_ACCESS_KEY") ?? "test";

            var config = new AmazonS3Config
            {
                ServiceURL = serviceUrl,
                ForcePathStyle = true,
                AuthenticationRegion = region,
                UseHttp = serviceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase),
            };

            return new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), config);
        }

        var regionEndpoint = RegionEndpoint.GetBySystemName(region);
        return new AmazonS3Client(regionEndpoint);
    }
}
