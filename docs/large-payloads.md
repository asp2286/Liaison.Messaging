# Large Payload Strategy

## Baseline: Externalization (Claim-Check Pattern)

The baseline strategy for large messages is claim-check externalization.

- Messages above a configured threshold (for example, 200 KB) are externalized to an `IPayloadStore`.
- The transport envelope body is sent empty, and the payload is referenced through headers.
- Core headers for this mode:
  - `liaison.payload.mode = external`
  - `liaison.payload.ref`
  - `liaison.payload.sha256`
  - `liaison.payload.size` (optional)
  - `liaison.payload.encoding` (optional)
- `IPayloadStore` is transport-agnostic. Core depends on the abstraction, not a storage product.
- Core does not know or care whether storage is Blob, S3, database, or another provider.
- This is the recommended and default large-payload strategy in Core.

Storage SDKs may use multipart upload internally. That is an implementation detail of storage IO and is not protocol-level message chunking.

## Non-Goal: Broker-Level Chunking in Core

Broker-level chunking is explicitly out of scope for `Liaison.Messaging` Core.

- Core does not implement broker-level chunking.
- Core does not model sequence numbers, chunk indexes, or reassembly state.
- Core remains stateless and transport-agnostic.
- There is no automatic fallback from external storage mode to broker chunking mode.

Why this is a non-goal in Core:

- Chunking behavior differs by broker semantics (for example, Kafka partitions, RabbitMQ ordering, Azure Service Bus sessions).
- Reliable chunking and reassembly require stateful coordination and transport-aware recovery logic.
- Those concerns belong in provider-specific extensions, not transport-agnostic Core.

## Future: Provider-Specific Broker Chunking

The following payload mode is reserved for future provider-specific extensions:

`liaison.payload.mode = chunked`

Potential headers for that future mode (reserved names):

- `liaison.payload.chunk.count`
- `liaison.payload.chunk.index`
- `liaison.payload.chunk.correlation`
- `liaison.payload.chunk.total-size`
- `liaison.payload.sha256`

Rules for this reserved surface:

- These headers are reserved for future use.
- Core does not interpret these headers.
- Providers may implement chunking strategies explicitly.
- Any chunking strategy must be opt-in.

## Design Constraints

- Large payload handling must be explicit.
- No implicit transport fallback.
- No CQRS semantics introduced.
- No type-based routing.
- No broker entity auto-creation.
- Headers remain `string:string` for forward compatibility.
