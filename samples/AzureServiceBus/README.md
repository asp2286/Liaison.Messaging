# Azure Service Bus Samples

## What the sample shows

- Pub/Sub with a regular small event over Azure Service Bus
- Request/Reply with a small inline message
- Request/Reply with a large message that crosses the threshold and uses external storage
- Two storage backends for the large request/reply scenario: Azure Blob and AWS S3

## Prerequisites

- Azure Service Bus namespace (Standard or Premium)
- Azure Blob container for the Azure Blob sample
- S3 bucket for the AWS S3 sample
- All entities must be created manually
- No auto-create behavior

## Required Azure Service Bus entities

### Pub/Sub

- Topic: `event.userregistered`
- Subscription: `pubsub-subscriber`

### Request/Reply Azure Blob

- Queue: `request.generatereport.azureblob`
- Queue: `request.generatereport.azureblob.reply`
- Queue: `request.uploaddataset.azureblob`
- Queue: `request.uploaddataset.azureblob.reply`

### Request/Reply AWS S3

- Queue: `request.generatereport.awss3`
- Queue: `request.generatereport.awss3.reply`
- Queue: `request.uploaddataset.awss3`
- Queue: `request.uploaddataset.awss3.reply`

## Running the samples

### A. Pub/Sub

Terminal 1:

```bash
dotnet run --project PubSub.Subscriber
```

Terminal 2:

```bash
dotnet run --project PubSub.Publisher
```

### B. Request/Reply with Azure Blob

Terminal 1:

```bash
dotnet run --project RequestReply.AzureBlob.Service
```

Terminal 2:

```bash
dotnet run --project RequestReply.AzureBlob.Client
```

### C. Request/Reply with AWS S3

Terminal 1:

```bash
dotnet run --project RequestReply.AwsS3.Service
```

Terminal 2:

```bash
dotnet run --project RequestReply.AwsS3.Client
```

## Expected behavior

- Pub/Sub prints a received `UserRegistered` event
- In each request/reply pair, the first request is small and stays inline in Service Bus
- In each request/reply pair, the second request is large and crosses the 100 KB threshold
- The handler still receives the original payload transparently

## Notes

- Do not commit real connection strings or credentials
- `appsettings.json` files contain placeholders only
- In CI or shared environments, prefer environment variables
- The sample intentionally keeps all DI wiring visible in `Program.cs`
