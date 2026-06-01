# Liaison.Messaging.Serialization.Protobuf

Optional Protocol Buffers `IMessageSerializer` implementation for `Google.Protobuf` generated message types.

## Install

```bash
dotnet add package Liaison.Messaging.Serialization.Protobuf
```

## DI

```csharp
using Liaison.Messaging;

services.AddProtobufMessageSerializer();
```

## Notes

- JSON remains the default serializer when using `AddLiaisonMessagingDefaults()`.
- `ProtobufMessageSerializer` requires `Google.Protobuf` generated message types.
- Non-protobuf types throw `InvalidOperationException` at runtime because `IMessageSerializer` is intentionally unconstrained.
