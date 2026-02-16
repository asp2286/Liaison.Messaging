# Liaison.Messaging.AwsStorage

AWS S3 `IPayloadStore` provider for large payload claim-check.

## Install

```bash
dotnet add package Liaison.Messaging.AwsStorage
```

## DI

```csharp
using Amazon.S3;
using Liaison.Messaging;
using Liaison.Messaging.AwsStorage;

services.AddS3PayloadStore(options =>
{
    options.Client = new AmazonS3Client();
    options.BucketName = "liaison-payloads";
    options.Prefix = "payload";
    options.Overwrite = false;
    options.SupportsConditionalPut = true;
});
```

## Notes

- `Overwrite = false` is atomic only when `SupportsConditionalPut = true` and the bucket supports `If-None-Match: *`.
- Directory buckets and other configurations that do not support conditional PUT must set `SupportsConditionalPut = false`.
- When `Overwrite = false` and `SupportsConditionalPut = false`, uploads throw `NotSupportedException` (no fallback logic, no HEAD+PUT).
- Bucket creation is never automatic; the configured bucket must already exist.
