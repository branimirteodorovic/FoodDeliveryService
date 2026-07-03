---
description: Add a domain event and its handler that publishes an integration event via the outbox and MassTransit/RabbitMQ. Use when an aggregate state change needs to notify other microservices.
disable-model-invocation: true
argument-hint: [ModuleName] [Action] [Entity]
---

# Add Domain Event

Arguments: `$ARGUMENTS` — format: `{ModuleName} {Action} {Entity}` (e.g. `Orders OrderPlaced Order` or `Users UserRegistered User`)

Reference (complete working example):
- Event: `src/Modules/Users/FoodDeliveryService.Modules.Users.Domain/Users/UserRegisteredDomainEvent.cs`
- Handler: `src/Modules/Users/FoodDeliveryService.Modules.Users.Application/Users/RegisterUser/UserRegisteredDomainEventHandler.cs`
- Contract: `src/Modules/Users/FoodDeliveryService.Modules.Users.IntegrationEvents/UserRegisteredIntegrationEvent.cs`

## Flow being wired
`Raise(...)` in aggregate → `InsertOutboxMessagesInterceptor` writes `outbox_messages` (same transaction) → `ProcessOutboxJob` (Quartz) → domain event handler → `IEventBus.PublishAsync` → MassTransit → RabbitMQ → consuming services' inboxes.

## Files to Create

### 1. Domain Event — in Domain project
Path: `src/Modules/{ModuleName}/FoodDeliveryService.Modules.{ModuleName}.Domain/{Entity}s/{Action}DomainEvent.cs`

```csharp
using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.{ModuleName}.Domain.{Entity}s;

public sealed class {Action}DomainEvent(Guid {entity}Id) : DomainEvent
{
    public Guid {Entity}Id { get; init; } = {entity}Id;
}
```

### 2. Raise in Aggregate
In the `{Entity}.cs` domain class, add to the relevant state-changing method:
```csharp
Raise(new {Action}DomainEvent(Id));
```

### 3. Domain Event Handler — in Application project
Path: `src/Modules/{ModuleName}/FoodDeliveryService.Modules.{ModuleName}.Application/{Entity}s/{UseCase}/{Action}DomainEventHandler.cs`

```csharp
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.{ModuleName}.Domain.{Entity}s;
using FoodDeliveryService.Modules.{ModuleName}.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.{ModuleName}.Application.{Entity}s.{UseCase};

internal sealed class {Action}DomainEventHandler(ISender sender, IEventBus eventBus)
    : DomainEventHandler<{Action}DomainEvent>
{
    public override async Task Handle({Action}DomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        // Fetch full data snapshot needed for the integration event
        Result<{Entity}Response> result = await sender.Send(
            new Get{Entity}Query(domainEvent.{Entity}Id), cancellationToken);

        if (result.IsFailure)
            throw new Common.Application.Exceptions.ApplicationException(nameof(Get{Entity}Query), result.Error);

        // Publish to RabbitMQ via MassTransit — the outbox job retries on exception
        await eventBus.PublishAsync(
            new {Action}IntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                result.Value.Id,
                // ... other fields from result.Value
            ),
            cancellationToken);
    }
}
```

### 4. Integration Event Contract — in IntegrationEvents project
Path: `src/Modules/{ModuleName}/FoodDeliveryService.Modules.{ModuleName}.IntegrationEvents/{Action}IntegrationEvent.cs`

```csharp
using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.{ModuleName}.IntegrationEvents;

public sealed class {Action}IntegrationEvent : IntegrationEvent
{
    public {Action}IntegrationEvent(Guid id, DateTime occurredOnUtc, Guid {entity}Id, ...)
        : base(id, occurredOnUtc)
    {
        {Entity}Id = {entity}Id;
        // include all data that consuming services need — be a complete snapshot
    }

    public Guid {Entity}Id { get; init; }
    // other properties as needed
}
```

## Notes
- The domain event handler is discovered automatically by `AddDomainEventHandlers()` in `{ModuleName}Module.cs` and decorated with `IdempotentDomainEventHandler`
- If the event is purely internal to the module (no other service cares), skip steps 3–4 — a domain event without an integration event is fine
- Always include a complete data snapshot in the integration event — consuming services CANNOT call back (separate databases, message-bus-only communication)
- To consume this event in another service, run `/add-integration-event`
- MassTransit propagates the OpenTelemetry trace context, so the publish→consume hop shows up in Jaeger automatically
