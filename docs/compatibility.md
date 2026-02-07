# Compatibility Notes

This document captures future-facing differences between Azure Service Bus (ASB) and AWS SQS and how the public API can stay transport-neutral.

## Design Direction

- Expose common capabilities through shared abstractions.
- Surface transport-specific behavior through explicit options and capability checks.
- Avoid pretending all transports are equivalent when semantics differ.

## Feature Matrix

| Capability | Azure Service Bus | AWS SQS | Abstraction Approach |
| --- | --- | --- | --- |
| Native pub/sub fan-out | Yes (topics/subscriptions) | Indirect (SNS + SQS) | Publish abstraction with provider-specific topology setup |
| Request/reply pattern | Yes (queues/topics + correlation) | Yes (queues + correlation/attributes) | Request/reply contracts with explicit correlation metadata |
| Delayed delivery | Yes | Yes | Unified delay option with provider bounds validation |
| FIFO ordering | Session/partition-specific behavior | FIFO queues supported | Ordering contract with capability flags |
| Dead-letter queue | Native DLQ support | Native DLQ support | Standard failure policy + provider mapping |
| Transaction scope | Supported in transport features | Limited/none equivalent | Optional transactional feature surfaced as capability |

## Notes

- Some behaviors require topology resources outside one package (for example SNS with SQS).
- Capability checks should fail fast at startup when unsupported features are requested.
