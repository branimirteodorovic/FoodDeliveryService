---
description: Add a domain event and its handler that publishes an integration event via DAPR. Use when an aggregate state change needs to notify other parts of the system.
disable-model-invocation: true
argument-hint: [ModuleName] [Action] [Entity]
---

# Add Domain Event

Arguments: `$ARGUMENTS` — format: `{ModuleName} {Action} {Entity}` (e.g. `Orders Placed Order` or `Restaurants Created Restaurant`)

Reference:
- Event: `evently_source_code/evently/src/Modules/Events/Evently.Modules.Events.Domain/Events/EventCreatedDomainEvent.cs`
- Handler: `evently_source_code/evently/src/Modules/Events/Evently.Modules.Events.Application/Events/PublishEvent/EventPublishedDomainEventHandler.cs`

## Files to Create

### 1. Domain Event — in Domain project
Path: `src/Modules/{ModuleName}/FoodDelivery.Modules.{ModuleName}.Domain/{Entity}s/{Action}DomainEvent.cs`

```csharp
using FoodDelivery.Common.Domain;

namespace FoodDelivery.Modules.{ModuleName}.Domain.{Entity}s;

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
Path: `src/Modules/{ModuleName}/FoodDelivery.Modules.{ModuleName}.Application/{Entity}s/{Action}/`

```csharp
using FoodDelivery.Common.Application.EventBus;
using FoodDelivery.Common.Application.Exceptions;
using FoodDelivery.Common.Application.Messaging;
using FoodDelivery.Common.Domain;
using FoodDelivery.Modules.{ModuleName}.Domain.{Entity}s;
using FoodDelivery.Modules.{ModuleName}.IntegrationEvents;
using MediatR;

namespace FoodDelivery.Modules.{ModuleName}.Application.{Entity}s.{Action};

internal sealed class {Action}DomainEventHandler(ISender sender, IEventBus eventBus)
    : DomainEventHandler<{Action}DomainEvent>
{
    public override async Task Handle({Action}DomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        // Fetch full data snapshot needed for the integration event
        Result<{Entity}Response> result = await sender.Send(
            new Get{Entity}Query(domainEvent.{Entity}Id), cancellationToken);

        if (result.IsFailure)
            throw new FoodDeliveryException(nameof(Get{Entity}Query), result.Error);

        // Publish to DAPR pub/sub — other modules subscribe via WithTopic()
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
Path: `src/Modules/{ModuleName}/FoodDelivery.Modules.{ModuleName}.IntegrationEvents/{Action}IntegrationEvent.cs`

```csharp
using FoodDelivery.Common.Application.EventBus;

namespace FoodDelivery.Modules.{ModuleName}.IntegrationEvents;

public sealed class {Action}IntegrationEvent : IntegrationEvent
{
    public {Action}IntegrationEvent(Guid id, DateTime occurredOnUtc, Guid {entity}Id, ...)
        : base(id, occurredOnUtc)
    {
        {Entity}Id = {entity}Id;
        // include all data that consuming modules need — be a complete snapshot
    }

    public Guid {Entity}Id { get; init; }
    // other properties as needed
}
```

## Notes
- The domain event handler is discovered automatically by `AddDomainEventHandlers()` in the module registration
- The integration event is published via `IEventBus` → `DaprEventBus` → DAPR pub/sub
- Always include a complete data snapshot in the integration event (consuming modules should not need to call back)
- The `ProcessOutboxJob` dispatches domain event handlers asynchronously for eventual consistency
