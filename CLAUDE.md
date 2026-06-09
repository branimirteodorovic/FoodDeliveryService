# FoodDeliveryService

## Overview
A Food Delivery Service built as a modular monolith in .NET 9, designed to evolve toward microservices.
Follows Domain-Driven Design, CQRS, and Event-Driven patterns.

**Reference implementation:** `evently_source_code/evently/` — study this before implementing any feature.
Namespace prefix is `FoodDelivery.*` (not `Evently.*`).

## Solution Structure
```
src/
├── API/
│   └── FoodDelivery.Api/                              # Single entry point
├── Common/
│   ├── FoodDelivery.Common.Domain/                    # Entity, Result<T>, Error, DomainEvent
│   ├── FoodDelivery.Common.Application/               # ICommand, IQuery, pipeline behaviors
│   ├── FoodDelivery.Common.Infrastructure/            # EF interceptors, Outbox/Inbox, DAPR EventBus, auth
│   └── FoodDelivery.Common.Presentation/              # IEndpoint, EndpointExtensions, ApiResults
└── Modules/
    └── {Name}/
        ├── FoodDelivery.Modules.{Name}.Domain/              # Entities, aggregates, value objects, events
        ├── FoodDelivery.Modules.{Name}.Application/         # Commands, queries, handlers, validators
        ├── FoodDelivery.Modules.{Name}.Infrastructure/      # DbContext, repos, Quartz jobs
        ├── FoodDelivery.Modules.{Name}.Presentation/        # Minimal API endpoints, integration handlers
        └── FoodDelivery.Modules.{Name}.IntegrationEvents/   # Cross-module event contracts
```

## Core Patterns

### CQRS with MediatR
- **Commands**: `sealed record XCommand(...) : ICommand<TResponse>` → `internal sealed class XCommandHandler : ICommandHandler<XCommand, TResponse>`
- **Queries**: `sealed record XQuery(...) : IQuery<TResponse>` → `internal sealed class XQueryHandler : IQueryHandler<XQuery, TResponse>`
- All return `Result<T>` — never throw for business failures
- MediatR pipeline behaviors: ExceptionHandling → RequestLogging → Validation

### Domain-Driven Design
- Aggregates extend `Entity` (from `FoodDelivery.Common.Domain`)
- `private` constructor + `private set` on all properties + static factory method
- Business logic **ONLY** in domain entities — handlers only orchestrate
- Raise domain events: `Raise(new {Action}DomainEvent(Id))` on every state change
- `{Entity}Errors` static class holds all `Error` definitions for that aggregate

### Railway-Oriented Programming
```csharp
// Domain entity method
public Result Cancel(DateTime utcNow) {
    if (Status == OrderStatus.Canceled) return Result.Failure(OrderErrors.AlreadyCanceled);
    Status = OrderStatus.Canceled;
    Raise(new OrderCanceledDomainEvent(Id));
    return Result.Success();
}
// Endpoint
return result.Match(Results.Ok, ApiResults.Problem);
```

### Event-Driven (Outbox → DAPR → Inbox)
1. Domain entity raises event → `InsertOutboxMessagesInterceptor` saves to `outbox_messages`
2. `ProcessOutboxJob` (Quartz) dispatches `IDomainEventHandler<T>` implementations
3. Domain event handler calls `IEventBus.PublishAsync(integrationEvent)` → DAPR pub/sub
4. Other modules subscribe via Minimal API endpoints with `.WithTopic("fooddelivery-pubsub", nameof(XEvent))`
5. Inbox idempotency via `inbox_messages` table + `ProcessInboxJob`

### Minimal API Endpoints
```csharp
internal sealed class CreateOrder : IEndpoint {
    public void MapEndpoint(IEndpointRouteBuilder app) {
        app.MapPost("orders", async (Request request, ISender sender) => {
            var result = await sender.Send(new CreateOrderCommand(...));
            return result.Match(Results.Ok, ApiResults.Problem);
        }).RequireAuthorization(Permissions.CreateOrder).WithTags(Tags.Orders);
    }
}
```
Discovered via `AddEndpoints(Presentation.AssemblyReference.Assembly)` — no manual registration.

## Identity: Duende IdentityServer Community Edition

**NOT Keycloak.** Integrated as part of the application:
- `IIdentityProviderService` abstraction in Application layer (same as evently pattern)
- Infrastructure implementation uses `UserManager<ApplicationUser>` from ASP.NET Identity
- Duende IdentityServer issues JWT tokens (authority: `https://localhost:5001`)
- JWT Bearer auth validates against Duende's discovery endpoint

## Messaging: DAPR (replaces MassTransit)

- `IEventBus` implemented by `DaprEventBus` using `DaprClient.PublishEventAsync()`
- Pub/sub component: `"fooddelivery-pubsub"` (backed by Redis Streams in Docker)
- Integration event consumers: `IEndpoint` classes with `.WithTopic(pubSubName, topicName)`
- `app.MapSubscribeHandler()` in Program.cs exposes `/dapr/subscribe`
- DAPR sidecar runs alongside the application in Docker

## Naming Conventions
| Item | Convention | Example |
|------|-----------|---------|
| Domain entity | `sealed class {Name} : Entity` | `Order` |
| Domain event | `{Action}DomainEvent` | `OrderPlacedDomainEvent` |
| Integration event | `{Action}IntegrationEvent` | `OrderPlacedIntegrationEvent` |
| Command | `{Action}{Entity}Command` | `PlaceOrderCommand` |
| Query | `Get{Entity}Query` / `Get{Entities}Query` | `GetOrderQuery` |
| Endpoint class | HTTP verb + resource | `CreateOrder`, `GetOrders` |
| Errors class | `{Entity}Errors` | `OrderErrors.NotFound(id)` |
| DB schema | lowercase module name | `orders`, `users`, `restaurants` |

## Hard Rules (always enforced)
1. **Domain logic MUST live in Domain projects** — never in command handlers
2. **Query handlers MUST use Dapper** via `IDbConnectionFactory` — never EF Core `DbSet<T>` for reads
3. **Never expose domain entities** in API responses — use response record DTOs
4. **Cross-module communication ONLY via integration events** — no direct module-to-module references
5. **Each module has its own DbContext and schema** — never query another module's tables
6. **Always `IUnitOfWork.SaveChangesAsync()`** — never `DbContext.SaveChanges()`
7. **DAPR pub/sub** for integration events — not MassTransit
8. **Duende IdentityServer** for authentication — not Keycloak

## Reference Files in evently_source_code
| Pattern | Reference File |
|---------|---------------|
| Aggregate | `src/Modules/Events/...Domain/Events/Event.cs` |
| Command handler | `src/Modules/Events/...Application/Events/CreateEvent/CreateEventCommandHandler.cs` |
| Query handler (Dapper) | `src/Modules/Events/...Application/Categories/GetCategories/GetCategoriesQueryHandler.cs` |
| Minimal API endpoint | `src/Modules/Events/...Presentation/Events/CreateEvent.cs` |
| Domain event → integration event | `src/Modules/Events/...Application/Events/PublishEvent/EventPublishedDomainEventHandler.cs` |
| Module registration | `src/Modules/Events/...Infrastructure/EventsModule.cs` |
| Entity base + Result<T> | `src/Common/Evently.Common.Domain/` |
| Pipeline behaviors | `src/Common/Evently.Common.Application/` |

## Build & Run
```bash
dotnet build
dotnet run --project src/API/FoodDelivery.Api
docker-compose up -d   # PostgreSQL, Redis, Seq, Jaeger, DAPR sidecar, Duende IdentityServer
dotnet ef migrations add {Name} --project src/Modules/{Module}/...Infrastructure
```

## Activity Log
Claude's actions are logged to `.claude/activity.log` — verify that CLAUDE.md and relevant files are read on each prompt.
