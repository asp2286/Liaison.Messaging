# Liaison.Messaging.AzureStorage

Azure Blob Storage `IPayloadStore` provider for large payload claim-check.

## Install

```bash
dotnet add package Liaison.Messaging.AzureStorage
```

## DI

```csharp
using Azure.Storage.Blobs;
using Liaison.Messaging;
using Liaison.Messaging.AzureStorage;

services.AddAzureBlobPayloadStore(options =>
{
    options.Client = new BlobServiceClient("<connection-string>");
    options.ContainerName = "liaison-payloads";
    options.Prefix = "payload";
    options.Overwrite = false;
});
```

## Notes

- `Overwrite = false` uses an atomic conditional write (`If-None-Match: *`).
- Container creation is never automatic; the configured container must already exist.
