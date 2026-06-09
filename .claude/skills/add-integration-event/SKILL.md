---
description: Add a cross-module integration event consumer for FoodDeliveryService. Use when one module needs to react to events published by another module via DAPR pub/sub.
disable-model-invocation: true
argument-hint: [PublishingModule] [ConsumingModule] [EventName]
---

# Add Integration Event Consumer

Arguments: `$ARGUMENTS` — format: `{PublishingModule} {ConsumingModule} {EventName}` (e.g. `Orders Notifications OrderPlaced`)

Reference:
- Consumer: `evently_source_code/evently/src/Modules/Attendance/Evently.Modules.Attendance.Presentation/Events/EventPublishedIntegrationEventHandler.cs`
- Integration event: `evently_source_code/evently/src/Modules/Events/Evently.Modules.Events.IntegrationEvents/EventPublishedIntegrationEvent.cs`

## Files to Create / Modify

### 1. Integration Event Contract (if not already exists)
Path: `src/Modules/{PublishingModule}/FoodDelivery.Modules.{PublishingModule}.IntegrationEvents/{EventName}IntegrationEvent.cs`

```csharp
using FoodDelivery.Common.Application.EventBus;

namespace FoodDelivery.Modules.{PublishingModule}.IntegrationEvents;

public sealed class {EventName}IntegrationEvent : IntegrationEvent
{
    public {EventName}IntegrationEvent(Guid id, DateTime occurredOnUtc, ...)
        : base(id, occurredOnUtc)
    {
        // Include all data consuming modules need
    }

    public Guid SomeId { get; init; }
    // ... other properties
}
```

### 2. Project Reference
In `FoodDelivery.Modules.{ConsumingModule}.Presentation.csproj`, add:
```xml
<ProjectReference Include="..\..\..\..\{PublishingModule}\FoodDelivery.Modules.{PublishingModule}.IntegrationEvents\FoodDelivery.Modules.{PublishingModule}.IntegrationEvents.csproj" />
```

### 3. Integration Event Consumer Endpoint
Path: `src/Modules/{ConsumingModule}/FoodDelivery.Modules.{ConsumingModule}.Presentation/{Entity}s/{EventName}IntegrationEventHandler.cs`

```csharp
using FoodDelivery.Common.Application.Exceptions;
using FoodDelivery.Common.Domain;
using FoodDelivery.Common.Presentation.Endpoints;
using FoodDelivery.Modules.{PublishingModule}.IntegrationEvents;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace FoodDelivery.Modules.{ConsumingModule}.Presentation.{Entity}s;

internal sealed class {EventName}IntegrationEventHandler(ISender sender) : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/dapr/subscribe/{consuming-module}/{event-topic}",
            async ([FromBody] {EventName}IntegrationEvent @event, ISender sender) =>
            {
                Result result = await sender.Send(
                    new Create{LocalEntity}Command(@event.SomeId, ...),
                    CancellationToken.None);

                if (result.IsFailure)
                    throw new FoodDeliveryException(nameof(Create{LocalEntity}Command), result.Error);

                return Results.Ok();
            })
            .WithTopic("fooddelivery-pubsub", nameof({EventName}IntegrationEvent))
            .ExcludeFromDescription(); // hide from Swagger — internal subscription endpoint
    }
}
```

### 4. Add Inbox Idempotency (if needed)
For idempotent processing, the `IdempotentIntegrationEventHandler<T>` decorator is applied automatically
by `AddIntegrationEventHandlers()` in the module registration. No extra work needed.

## Notes
- The DAPR topic name MUST match `nameof({EventName}IntegrationEvent)` on both publisher and consumer
- `app.MapSubscribeHandler()` in Program.cs exposes `/dapr/subscribe` so DAPR can discover subscriptions
- The inbox pattern prevents double-processing if DAPR delivers the same event twice
- One integration event can have multiple consumers in different modules — each module has its own handler
- Return `Results.Ok()` from the endpoint — DAPR marks the message as consumed; exception = retry
