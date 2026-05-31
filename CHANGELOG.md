# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

[0.1.0]: https://github.com/asp2286/Liaison.Messaging/releases/tag/0.1.0
