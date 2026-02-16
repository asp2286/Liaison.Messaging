# Payload Store Contract Tests

These tests are integration-style contract tests for:

- `Liaison.Messaging.AzureStorage`
- `Liaison.Messaging.AwsStorage`

Tests are capability-driven and can run without external services by leaving provider env vars unset.

## Azure (Azurite or real Azure)

- `LIAISON_TEST_AZURE_BLOB_CONNECTION_STRING`
- `LIAISON_TEST_AZURE_BLOB_CONTAINER`
- `LIAISON_TEST_AZURE_BLOB_PREFIX` (optional, default: `contract-tests`)
- `LIAISON_TEST_AZURITE_ENABLED` (optional, set to `1` to use Azurite defaults)

Azurite quick start:

```bash
docker run --rm -p 10000:10000 mcr.microsoft.com/azure-storage/azurite azurite-blob --blobHost 0.0.0.0
export LIAISON_TEST_AZURITE_ENABLED=1
export LIAISON_TEST_AZURE_BLOB_CONTAINER=liaison-payloadstore-tests
```

The container/bucket must already exist.

## S3 (LocalStack/MinIO or real AWS)

- `LIAISON_TEST_S3_BUCKET`
- `LIAISON_TEST_S3_PREFIX` (optional, default: `contract-tests`)
- `LIAISON_TEST_S3_REGION` (optional, default: `us-east-1`)
- `LIAISON_TEST_S3_SERVICE_URL` (optional for LocalStack/MinIO)
- `LIAISON_TEST_S3_ACCESS_KEY_ID` and `LIAISON_TEST_S3_SECRET_ACCESS_KEY` (optional, typically required for local emulators)
- `LIAISON_TEST_S3_SUPPORTS_CONDITIONAL_PUT` (optional, default: `true`)

If `LIAISON_TEST_S3_SUPPORTS_CONDITIONAL_PUT=false`, overwrite-false behavior is asserted as deterministic `NotSupportedException` (no fallback).
