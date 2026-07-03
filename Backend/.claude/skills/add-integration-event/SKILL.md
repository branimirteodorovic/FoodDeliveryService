---
description: Add a cross-service integration event consumer for FoodDeliveryService. Use when one microservice needs to react to events published by another service via MassTransit/RabbitMQ with the inbox pattern.
disable-model-invocation: true
argument-hint: [PublishingModule] [ConsumingModule] [EventName]
---

# Add Integration Event Consumer

Arguments: `$ARGUMENTS` — format: `{PublishingModule} {ConsumingModule} {EventName}` (e.g. `Orders Notifications OrderPlaced`)

Reference (working example — Orders consuming Users events):
- Consumer registration: `src/Modules/Orders/FoodDeliveryService.Modules.Orders.Infrastructure/OrdersModule.cs` (`ConfigureConsumers`)
- Generic inbox consumer: `src/Modules/Orders/FoodDeliveryService.Modules.Orders.Infrastructure/Inbox/IntegrationEventConsumer.cs`
- Contract: `src/Modules/Users/FoodDeliveryService.Modules.Users.IntegrationEvents/UserRegisteredIntegrationEvent.cs`

## Flow being wired
RabbitMQ → MassTransit `IntegrationEventConsumer<TEvent>` (writes `inbox_messages` only) → `ProcessInboxJob` (Quartz) → `IIntegrationEventHandler<TEvent>` in the consuming module's **Presentation** assembly (decorated with `IdempotentIntegrationEventHandler` — safe against duplicate delivery).

## Files to Create / Modify

### 1. Integration Event Contract (if it doesn't exist yet)
Path: `src/Modules/{PublishingModule}/FoodDeliveryService.Modules.{PublishingModule}.IntegrationEvents/{EventName}IntegrationEvent.cs`

```csharp
using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.{PublishingModule}.IntegrationEvents;

public sealed class {EventName}IntegrationEvent : IntegrationEvent
{
    public {EventName}IntegrationEvent(Guid id, DateTime occurredOnUtc, ...)
        : base(id, occurredOnUtc)
    {
        // Include all data consuming services need — full snapshot, no call-backs possible
    }

    public Guid SomeId { get; init; }
    // ... other properties
}
```

### 2. Project References
The consuming module references ONLY the publisher's IntegrationEvents project:
- In `FoodDeliveryService.Modules.{ConsumingModule}.Presentation.csproj` (for the handler)
- In `FoodDeliveryService.Modules.{ConsumingModule}.Infrastructure.csproj` (for the consumer registration)
```xml
<ProjectReference Include="..\..\Users\FoodDeliveryService.Modules.{PublishingModule}.IntegrationEvents\FoodDeliveryService.Modules.{PublishingModule}.IntegrationEvents.csproj" />
```

### 3. Register the MassTransit Consumer — consuming module Infrastructure
In `src/Modules/{ConsumingModule}/FoodDeliveryService.Modules.{ConsumingModule}.Infrastructure/{ConsumingModule}Module.cs`, inside `ConfigureConsumers`:

```csharp
registrationConfigurator.AddConsumer<IntegrationEventConsumer<{EventName}IntegrationEvent>>()
    .Endpoint(c => c.InstanceId = instanceId);   // REQUIRED — gives this service its own queue
```
`IntegrationEventConsumer<T>` already exists in the module's `Infrastructure/Inbox/` — it only persists the message to `inbox_messages`. Do not add logic to it.

### 4. Integration Event Handler — consuming module Presentation
Path: `src/Modules/{ConsumingModule}/FoodDeliveryService.Modules.{ConsumingModule}.Presentation/{Entity}s/{EventName}IntegrationEventHandler.cs`

```csharp
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.{PublishingModule}.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.{ConsumingModule}.Presentation.{Entity}s;

internal sealed class {EventName}IntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<{EventName}IntegrationEvent>
{
    public override async Task Handle(
        {EventName}IntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new {LocalAction}Command(integrationEvent.SomeId, ...),
            cancellationToken);

        if (result.IsFailure)
            throw new Common.Application.Exceptions.ApplicationException(nameof({LocalAction}Command), result.Error);
    }
}
```
Discovered automatically by `AddIntegrationEventHandlers()` in `{ConsumingModule}Module.cs` (scans the Presentation assembly) and decorated with `IdempotentIntegrationEventHandler`. The `{LocalAction}Command` is a normal command in the consuming module's Application layer — create it with `/add-command` if missing.

## Notes
- Idempotency is automatic (inbox table + idempotent decorator) — duplicate deliveries are ignored
- Throwing from the handler leaves the inbox message unprocessed → `ProcessInboxJob` retries
- One integration event can have consumers in several services — each registers its own `IntegrationEventConsumer<T>` and handler
- Typical use: replicate publisher data into the consuming module's own tables so its queries never cross service boundaries
- If this event is part of a multi-step workflow (3+ services, compensation, timeouts), consider a MassTransit saga instead of chaining handlers — see the commented `AddSagaStateMachine` scaffold in `OrdersModule.ConfigureConsumers`
- Trace context flows through MassTransit automatically — verify the full publish→consume chain in Jaeger (http://localhost:16686)
