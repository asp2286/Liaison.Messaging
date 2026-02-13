# Liaison.Messaging

Liaison.Messaging is an open-source family of provider-agnostic .NET libraries for out-of-process messaging.

## Scope

- `Liaison.Mediator`: in-process dispatch.
- `Liaison.Messaging`: out-of-process messaging primitives (`pub/sub` and `request/reply`).

The design targets explicit configuration, predictable behavior, and provider isolation.

## Providers

- First provider: Azure Service Bus.
- Planned provider: AWS SQS.

## Repository Structure

- `src/Liaison.Messaging.Abstractions`: contracts and core messaging abstractions.
- `src/Liaison.Messaging.Core`: shared runtime primitives and policies.
- `src/Liaison.Messaging.DependencyInjection`: registration extensions.
- `src/Liaison.Messaging.Hosting`: hosting integration.
- `src/Liaison.Messaging.AzureServiceBus`: Azure Service Bus provider.
- `src/Liaison.Messaging.AwsSqs`: AWS SQS provider (planned expansion).
- `samples/`: minimal usage samples.
- `docs/`: roadmap and architecture notes.

## Documentation

- Project Direction: `docs/roadmap.md`
- Routing & Semantics: `docs/routing.md`
- Large Payload Strategy → `docs/large-payloads.md`

## Project Direction

See `docs/roadmap.md`.
