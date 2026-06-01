# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2026-06-01

### Added
- `Liaison.Messaging.Serialization.Protobuf`: optional Protocol Buffers serializer
  (`Google.Protobuf`) for cross-language wire compatibility. JSON remains the default.
- Added `net10.0` target framework across all libraries; `net8.0` and
  `netstandard2.0` remain supported.

### Changed
- Updated `Microsoft.Extensions.*` to 10.x and `System.Text.Json` to 10.x. This
  raises the transitive dependency floor: consumers, including those targeting
  .NET 8, will now resolve `Microsoft.Extensions.*` 10.x. `netstandard2.0`
  support is retained because the 10.x packages ship a `netstandard2.0` asset.
- Updated `Azure.Storage.Blobs` to 12.28.x.

## [0.1.0] - 2026-06-01

Initial public release.

### Added
- Core messaging primitives: message envelope, message context, serializer
  abstraction, message id generator, envelope and context factories.
- `SystemTextJsonMessageSerializer` (public, in Core).
- Publish/subscribe, request/reply, and claim-check (large payload) patterns
  as transport-neutral abstractions.
- Large payload policy (`DefaultLargePayloadPolicy`) with threshold-based
  externalization, optional gzip compression, and SHA-256 integrity validation.
- Payload stores: Azure Blob Storage (`AzureBlobPayloadStore`) and
  AWS S3 (`AwsS3PayloadStore`).
- Azure Service Bus transport provider: publisher, subscription, request client,
  request processor, entity router, with explicit DI extensions.
- AWS SQS transport provider: publisher, subscription, request client, request
  processor, supporting Standard and FIFO queues, with explicit DI extensions.
- Hosted-service wrappers for subscriptions and request processors
  (Generic Host integration) in `Liaison.Messaging.Hosting`.
- `AddLiaisonMessagingDefaults()` DI extension for core service registration.
- In-memory transport implementation for testing and prototyping.

### Notes
- Targets .NET 8.0; core abstractions also target netstandard2.0.
- Design philosophy: explicit configuration, no assembly scanning, no auto
  creation of queues/topics/subscriptions, deterministic behavior, thin
  provider adapters.

[0.2.0]: https://github.com/asp2286/Liaison.Messaging/compare/0.1.0...0.2.0
[0.1.0]: https://github.com/asp2286/Liaison.Messaging/releases/tag/0.1.0
