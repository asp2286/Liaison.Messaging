# Routing

## Core Principle

`Liaison.Messaging` does not hard-code messaging semantics in core abstractions. Core APIs stay transport-agnostic and explicit.

## Point-to-Point Request/Reply

Request/reply is point-to-point and typically uses two queues:

- Request queue
- Reply queue

Replies are correlated using `CorrelationId`.

## Fan-Out Pub/Sub

Pub/sub notifications are fan-out and typically use topic + subscriptions.

An `event` label is a semantic hint for fan-out notification behavior, not a CQRS contract. There is no `IEvent` interface in core.

## Configuration Approaches

### A) Explicit Configuration (recommended default)

- `AzureServiceBusEntityOptions.Kind = Queue` or `Topic`
- `AzureServiceBusRequestReplyOptions` sets `RequestQueueName` and `ReplyQueueName`

### B) Optional Semantics Helper (provider-local)

A provider may optionally read a semantic header:

- `liaison.kind=event|command|request`

Possible provider-local mapping:

- `event` -> topic
- `command` -> queue
- `request` -> request queue

This helper is opt-in and does not change core abstractions.

## AWS SQS Compatibility Note

SQS has no native topic entity. Fan-out notification patterns can be mapped with SNS + SQS when needed.

## Non-Goals

- Type-based routing from CLR type names
- Naming conventions derived from message types
- CQRS interfaces (`IEvent`, `ICommand`, `IQuery`) in core
