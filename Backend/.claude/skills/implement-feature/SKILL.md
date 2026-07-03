---
description: Use when the user describes a feature, user story, or requirement to implement. Implements the feature following FoodDeliveryService DDD/CQRS microservices patterns (YARP gateway, MediatR, MassTransit/RabbitMQ, Duende IdentityServer, EF Core, Dapper, outbox/inbox). Automatically triggered when the user pastes a feature description or asks to implement something new.
---

# Implement Feature

## Pre-flight (always do these steps first)
1. Read `CLAUDE.md` to confirm current rules and patterns
2. Decide **which service owns the feature** (see Feature Analysis below)
3. Study the closest existing equivalent in this repo:
   - Domain: `src/Modules/Users/FoodDeliveryService.Modules.Users.Domain/Users/`
   - Command + validator + handler: `src/Modules/Users/...Users.Application/Users/RegisterUser/`
   - Query (Dapper): `src/Modules/Users/...Users.Application/Users/GetUser/`
   - Domain event → integration event: `...Users.Application/Users/RegisterUser/UserRegisteredDomainEventHandler.cs`
   - Consumer registration: `src/Modules/Orders/...Orders.Infrastructure/OrdersModule.cs` (`ConfigureConsumers`)
   - API host: `src/API/FoodDeliveryService.Orders.Api/Program.cs`

## Feature Analysis (answer before writing code)

**Ownership** — which service owns the state being changed?
- Users: registration, profiles, roles, permissions
- Orders: order lifecycle, cart, delivery status; has a local replica of user data
- Restaurants: restaurants, menus, availability
- Notifications: reactions to other services' events (email/push)
- A new bounded context → new microservice via `/add-module`

**Shape of the work:**
- What aggregate(s) are involved? New or existing?
- What commands (writes) and queries (reads) are needed?
- What domain events does each state change raise?

**Cross-service impact (critical in this architecture):**
- Does any OTHER service care about this state change? → integration event (outbox) + consumer in each interested service (inbox). Ask per service: does Notifications need to notify anyone? Does Orders/Restaurants need a local data replica?
- Does this feature need data OWNED by another service? → do NOT call it; consume its integration events and replicate locally (pattern: Orders consumes `UserRegistered`/`UserProfileUpdated`). If the event doesn't exist yet, add it to the owning service first
- Truly synchronous need? Only MassTransit request/response (`IRequestClient<T>`, pattern: `GetUserPermissionsRequest`) — use sparingly, cache in Redis if hot-path
- **Saga check**: does the flow span 3+ steps across services, need compensation/rollback, or timeouts (e.g. place order → restaurant confirms → delivery → notify)? Then PROPOSE a MassTransit state machine saga (`AddSagaStateMachine<TSaga, TState>().RedisRepository(...)` — commented scaffold in `OrdersModule.ConfigureConsumers`) instead of chaining event handlers, and explain the trade-off to the user before building it

**Edge concerns:**
- Endpoint route: falls under the module's existing YARP prefix (`{module}/**`)? If a new prefix, add route + cluster in the Gateway appsettings
- Auth: which permission guards the endpoint? Anonymous endpoints need an explicit `anonymous` YARP route (like `users/register`)
- Observability: MassTransit/EF/HTTP are auto-instrumented; any NEW external dependency needs OTel instrumentation + a health check in the host

## Implementation Order

### Step 1 — Domain Layer (`src/Modules/{Module}/FoodDeliveryService.Modules.{Module}.Domain/`)

**Aggregate / Entity:**
```csharp
public sealed class {Entity} : Entity
{
    private {Entity}() { }  // EF Core requires private parameterless ctor

    public Guid Id { get; private set; }
    // All properties: private set

    public static Result<{Entity}> Create(...) // Factory method
    {
        // Guard clauses → return Result.Failure<{Entity}>({Entity}Errors.XyzError)
        var entity = new {Entity} { Id = Guid.NewGuid(), ... };
        entity.Raise(new {Entity}CreatedDomainEvent(entity.Id));
        return entity;
    }

    public Result {BusinessAction}(...) // Business methods
    {
        if (/* invalid state */) return Result.Failure({Entity}Errors.InvalidState);
        // mutate state
        Raise(new {Action}DomainEvent(Id));
        return Result.Success();
    }
}
```

Also create: `{Entity}Errors` static class, one file per domain event, and `I{Entity}Repository` (all in Domain).

### Step 2 — Application Layer (`.../FoodDeliveryService.Modules.{Module}.Application/`)

- **Command** `public sealed record {Action}{Entity}Command(...) : ICommand<Guid>` + `internal sealed` handler (fetch → domain method → `unitOfWork.SaveChangesAsync`) + FluentValidation validator — see `/add-command`
- **Query** + `internal sealed` handler using **Dapper via `IDbConnectionFactory`** (snake_case tables, no schema prefix, own DB only) + response record — see `/add-query`
- **Domain event handler** (`DomainEventHandler<T>`) that publishes the integration event via `IEventBus.PublishAsync`; throw `Common.Application.Exceptions.ApplicationException` on failure so the outbox retries — see `/add-domain-event`

### Step 3 — Integration Events (`.../FoodDeliveryService.Modules.{Module}.IntegrationEvents/`)

```csharp
public sealed class {Action}IntegrationEvent : IntegrationEvent
{
    public {Action}IntegrationEvent(Guid id, DateTime occurredOnUtc, Guid {entity}Id, ...)
        : base(id, occurredOnUtc)
    {
        {Entity}Id = {entity}Id;
        // full snapshot — consumers cannot call back across services
    }
    public Guid {Entity}Id { get; init; }
}
```

### Step 4 — Infrastructure Layer (`.../FoodDeliveryService.Modules.{Module}.Infrastructure/`)

- `IEntityTypeConfiguration<{Entity}>` (`builder.ToTable("{table}")` — no schema; `UseSnakeCaseNamingConvention` handles columns)
- `{Entity}Repository` implementing the Domain interface via the module's DbContext
- Register repository in `{Module}Module.AddInfrastructure`
- If consuming another service's event: add `AddConsumer<IntegrationEventConsumer<TEvent>>().Endpoint(c => c.InstanceId = instanceId)` in `ConfigureConsumers` — see `/add-integration-event`
- Migration: `dotnet ef migrations add {Name} --project src/Modules/{Module}/FoodDeliveryService.Modules.{Module}.Infrastructure --startup-project src/API/FoodDeliveryService.{Module}.Api`

### Step 5 — Presentation Layer (`.../FoodDeliveryService.Modules.{Module}.Presentation/`)

**Endpoint:**
```csharp
internal sealed class {Action}{Entity} : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("{route-under-module-prefix}", async (Request request, ISender sender) =>
        {
            Result<Guid> result = await sender.Send(new {Action}{Entity}Command(...));
            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.{Action}{Entity})
        .WithTags(Tags.{Module});
    }

    internal sealed class Request { /* input DTO */ }
}
```

**Integration event handler** (if this module reacts to another service's events):
```csharp
internal sealed class {Event}IntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<{Event}IntegrationEvent>
{
    public override async Task Handle({Event}IntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(new {LocalAction}Command(integrationEvent....), cancellationToken);
        if (result.IsFailure)
            throw new Common.Application.Exceptions.ApplicationException(nameof({LocalAction}Command), result.Error);
    }
}
```
(Dispatched by `ProcessInboxJob`; the paired `IntegrationEventConsumer<T>` registration from Step 4 feeds the inbox.)

## Post-Implementation Checklist
- [ ] `dotnet build` — zero compilation errors
- [ ] Domain entity has no EF Core / MassTransit / MediatR using statements
- [ ] Query handlers use `IDbConnectionFactory` + Dapper (grep for `DbSet` in query files) and touch only this service's DB
- [ ] All commands/queries return `Result<T>`; endpoints use `result.Match(Results.Ok, ApiResults.Problem)`
- [ ] Domain event raised in every state-changing method
- [ ] Cross-service reactions: integration event published (outbox) AND consumer registered + handler added in every consuming service (inbox)
- [ ] Migration added for schema changes
- [ ] Route reachable through the Gateway; new prefixes added to YARP config
- [ ] Endpoint has `.RequireAuthorization(...)` (or an intentional anonymous YARP route)
- [ ] If a saga was warranted, it was proposed to the user
- [ ] Verify the flow end-to-end in Jaeger (http://localhost:16686) and Seq (http://localhost:8081) when running via `docker-compose up -d`
