# Roadmap

## Principles

- Determinism first: runtime behavior must be explicit and repeatable.
- Explicit configuration over discovery: no magic assembly scanning.
- Provider isolation: provider-specific concerns stay inside provider packages.
- Stable core contracts: abstractions should evolve conservatively.

## Current Focus

- Define `Liaison.Messaging.Abstractions` and `Liaison.Messaging.Core` as the stable base.
- Establish Azure Service Bus as the first production provider path.
- Keep configuration and registration APIs explicit and testable.

## Planned Milestones

## v0.1

- Baseline abstractions for message envelope, publish/subscribe, and request/reply.
- Core policy model for metadata and payload handling.
- Initial Azure Service Bus provider package structure.

## v0.2

- Refine diagnostics and context propagation hooks.
- Expand hosting and dependency injection integration.
- Harden provider contract boundaries and compatibility tests.

## v1.0

- API stabilization for abstractions and core contracts.
- Provider behavior guarantees documented and verified.
- Production-readiness baseline for the Azure Service Bus provider.

## AWS SQS Provider

- Add `Liaison.Messaging.AwsSqs` implementation aligned with core abstractions.
- Document intentional capability differences where SQS semantics diverge.

## Non-Goals

- Building a full service bus administration UI.
- Delivering a message schema registry platform.
- Introducing heavy framework dependencies.
- Relying on magical conventions or hidden runtime behavior.
