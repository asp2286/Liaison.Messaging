# Architecture

## Envelope-First Design

All transport operations are modeled around an envelope with payload plus transport-neutral metadata. Provider adapters map this envelope to transport-specific formats.

## Message Context

Context values are optional and explicit. Typical fields include:

- Correlation identifiers.
- Trace identifiers.
- Tenant identifiers.

Context is propagated as metadata, not inferred from ambient global state.

## Payload and Metadata Strategies

Payload handling is policy-driven so behavior can be selected per message type or endpoint. Examples include:

- Inline payload for small messages.
- Reference-based payload for large bodies.
- Metadata enrichment or filtering rules.

Policies live in core abstractions; providers execute the resulting transport mapping.

## Provider Responsibilities and Boundaries

Providers are responsible for:

- Connection/session lifecycle with the transport.
- Envelope serialization and metadata mapping.
- Delivery semantics mapping (acknowledgement, retry, settlement).

Providers are not responsible for:

- Core domain contracts.
- Cross-provider policy definitions.
- Application-specific business conventions.
