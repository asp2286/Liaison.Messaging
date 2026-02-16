namespace Liaison.Messaging.PayloadStores.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Liaison.Messaging;
using Liaison.Messaging.AzureStorage;

public sealed class AzureBlobPayloadStoreFixture : PayloadStoreFixture
{
    private const string ExpiresMetadataKey = "liaison-expires-at";
    private const string DefaultAzuriteConnectionString =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
        "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
        "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";

    private readonly string? _connectionString;
    private readonly string? _containerName;
    private readonly string _prefix;
    private readonly BlobContainerClient? _containerClient;

    public AzureBlobPayloadStoreFixture()
    {
        var azuriteEnabled = GetBooleanEnvironmentVariable("LIAISON_TEST_AZURITE_ENABLED", defaultValue: false);
        _connectionString = Environment.GetEnvironmentVariable("LIAISON_TEST_AZURE_BLOB_CONNECTION_STRING");
        _containerName = Environment.GetEnvironmentVariable("LIAISON_TEST_AZURE_BLOB_CONTAINER");
        _prefix = Environment.GetEnvironmentVariable("LIAISON_TEST_AZURE_BLOB_PREFIX") ?? "contract-tests";

        if (azuriteEnabled)
        {
            _connectionString ??= DefaultAzuriteConnectionString;
            _containerName ??= "liaison-payloadstore-tests";
        }

        IsEnabled = !string.IsNullOrWhiteSpace(_connectionString) && !string.IsNullOrWhiteSpace(_containerName);
        if (IsEnabled)
        {
            var client = new BlobServiceClient(_connectionString);
            _containerClient = client.GetBlobContainerClient(_containerName);
        }
    }

    public override string ProviderName => "AzureBlob";

    public override bool IsEnabled { get; }

    public override bool SupportsConditionalPut => true;

    public override bool CanDisableConditionalPut => false;

    public override bool CanVerifyExpiresMarker => IsEnabled;

    public override IPayloadStore CreateStore(
        bool overwrite = false,
        bool emitExpiresMarker = true,
        bool supportsConditionalPut = true)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("Azure payload-store fixture is not configured.");
        }

        return new AzureBlobPayloadStore(new AzureBlobPayloadStoreOptions
        {
            ConnectionString = _connectionString,
            ContainerName = _containerName!,
            Prefix = _prefix,
            Overwrite = overwrite,
            EmitExpiresMarker = emitExpiresMarker,
        });
    }

    public override string CreateReferencePrefix()
    {
        return $"contract/{Guid.NewGuid():N}";
    }

    public override async Task<string?> ReadExpiresMarkerAsync(string reference, CancellationToken ct = default)
    {
        if (!IsEnabled || _containerClient is null)
        {
            return null;
        }

        var blobClient = _containerClient.GetBlobClient(reference);
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: ct).ConfigureAwait(false);
        if (TryGetMetadataValueCaseInsensitive(properties.Value.Metadata, ExpiresMetadataKey, out var marker))
        {
            return marker;
        }

        return null;
    }

    private static bool TryGetMetadataValueCaseInsensitive(
        IDictionary<string, string> metadata,
        string key,
        out string? value)
    {
        foreach (var pair in metadata)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
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
}
