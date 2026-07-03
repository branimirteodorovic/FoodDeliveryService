# FoodDeliveryService

## Overview
A Food Delivery Service built as **.NET 9 microservices behind a YARP API gateway**.
Follows Domain-Driven Design, CQRS, Railway-Oriented Programming, and Event-Driven patterns.
Namespace prefix is `FoodDeliveryService.*`. All paths below are relative to `Backend/`.

> The old `evently_source_code` reference folder is gone. The reference implementation is now the codebase itself — the **Users module** is the most complete example; study it before implementing a feature.

## Solution Topology
```
src/
├── API/                                            # One host per service
│   ├── FoodDeliveryService.Gateway/                # YARP reverse proxy — single public entry point (:3000)
│   ├── FoodDeliveryService.Identity/               # Duende IdentityServer + ASP.NET Identity (:18080)
│   ├── FoodDeliveryService.Notifications.Api/      # hosts Notifications module (:5100)
│   ├── FoodDeliveryService.Orders.Api/             # hosts Orders module (:5200)
│   ├── FoodDeliveryService.Restaurants.Api/        # hosts Restaurants module (:5300)
│   └── FoodDeliveryService.Users.Api/              # hosts Users module (:5400)
├── Common/
│   ├── FoodDeliveryService.Common.Domain/          # Entity, Result<T>, Error, DomainEvent
│   ├── FoodDeliveryService.Common.Application/     # ICommand/IQuery, pipeline behaviors, IEventBus, IntegrationEvent
│   ├── FoodDeliveryService.Common.Infrastructure/  # AddInfrastructure: auth, MassTransit, Outbox/Inbox, OTel, Dapper, Redis
│   └── FoodDeliveryService.Common.Presentation/    # IEndpoint, EndpointExtensions, ApiResults
└── Modules/
    └── {Name}/                                     # Each module is deployed in exactly ONE API host
        ├── FoodDeliveryService.Modules.{Name}.Domain/             # Entities, aggregates, domain events, errors
        ├── FoodDeliveryService.Modules.{Name}.Application/        # Commands, queries, handlers, validators
        ├── FoodDeliveryService.Modules.{Name}.Infrastructure/     # DbContext, repos, Outbox/Inbox jobs, {Name}Module.cs
        ├── FoodDeliveryService.Modules.{Name}.Presentation/       # Minimal API endpoints, IIntegrationEventHandler impls
        └── FoodDeliveryService.Modules.{Name}.IntegrationEvents/  # Cross-service event/message contracts (the ONLY shared surface)
```

## Service Responsibilities (feature placement guide)
| Service / Module | Owns | Publishes | Consumes |
|---|---|---|---|
| **Users** | Registration, profiles, roles, permissions | `UserRegisteredIntegrationEvent`, `UserProfileUpdatedIntegrationEvent`; responds to `GetUserPermissionsRequest` (MassTransit request/response) | — |
| **Orders** | Order lifecycle (cart → placed → delivered/canceled), local replica of user data | Order lifecycle events (as added) | `UserRegistered`, `UserProfileUpdated` |
| **Restaurants** | Restaurants, menus, availability | Restaurant/menu events (as added) | (as needed) |
| **Notifications** | Sending notifications in reaction to events | — | Events from other services (as added) |
| **Identity** | Credentials, token issuance (Duende). NOT a module — plain host with local API `api/users` for user provisioning | — | — |

When implementing a feature, first decide: **which service owns the state being changed?** That module gets the command/endpoint. Then ask: **does any other service care about this state change?** If yes, publish an integration event and add consumers there — never call another service's API or database directly.

## Request Flow
1. Client → **Gateway** (`:3000`). YARP validates the JWT (Duende), then routes by path prefix: `orders/**`, `restaurants/**`, `users/**`, `notifications/**` → the matching service. `users/register` is anonymous.
2. Service validates the JWT again, `CustomClaimsTransformation` resolves permissions via `IPermissionService` (in non-Users services this is a **MassTransit request/response call to Users**, cached in Redis for 5 min).
3. Minimal API endpoint → `ISender.Send(command/query)` → `result.Match(Results.Ok, ApiResults.Problem)`.

New endpoints require a matching YARP route only if they introduce a **new path prefix** — existing `{module}/**` routes cover new endpoints automatically.

## Core Patterns

### CQRS with MediatR
- **Commands** (writes): `sealed record XCommand(...) : ICommand<TResponse>` → `internal sealed class XCommandHandler : ICommandHandler<XCommand, TResponse>` — EF Core repositories + `IUnitOfWork.SaveChangesAsync()`
- **Queries** (reads): `sealed record GetXQuery(...) : IQuery<TResponse>` → `internal sealed class GetXQueryHandler : IQueryHandler<GetXQuery, TResponse>` — **Dapper via `IDbConnectionFactory`**, never EF Core
- All return `Result<T>` — never throw for business failures
- Pipeline behaviors: ExceptionHandling → RequestLogging → Validation (FluentValidation)

### Domain-Driven Design
- Aggregates extend `Entity` (from `FoodDeliveryService.Common.Domain`)
- `private` constructor + `private set` on all properties + static factory method
- Business logic **ONLY** in domain entities — handlers only orchestrate
- Raise domain events: `Raise(new {Action}DomainEvent(Id))` on every state change
- `{Entity}Errors` static class holds all `Error` definitions for that aggregate

### Railway-Oriented Programming
```csharp
public Result Cancel(DateTime utcNow) {
    if (Status == OrderStatus.Canceled) return Result.Failure(OrderErrors.AlreadyCanceled);
    Status = OrderStatus.Canceled;
    Raise(new OrderCanceledDomainEvent(Id));
    return Result.Success();
}
// Endpoint
return result.Match(Results.Ok, ApiResults.Problem);
```

### Event-Driven: Outbox → MassTransit/RabbitMQ → Inbox
**Publish side (service A):**
1. Domain entity raises a domain event → `InsertOutboxMessagesInterceptor` saves it to `outbox_messages` in the same transaction
2. `ProcessOutboxJob` (Quartz) dispatches `IDomainEventHandler<T>` implementations (Application layer, wrapped in `IdempotentDomainEventHandler`)
3. The domain event handler builds a full-snapshot integration event and calls `IEventBus.PublishAsync(...)` → MassTransit publishes to RabbitMQ

**Consume side (service B):**
4. `IntegrationEventConsumer<TEvent>` (a MassTransit `IConsumer`, registered in `{Module}Module.ConfigureConsumers` with `.Endpoint(c => c.InstanceId = instanceId)` so each service gets its own queue) writes the event to `inbox_messages`
5. `ProcessInboxJob` (Quartz) dispatches `IIntegrationEventHandler<TEvent>` implementations found in the **Presentation** assembly (wrapped in `IdempotentIntegrationEventHandler`)

**Synchronous cross-service calls** exist only as MassTransit request/response (`IRequestClient<T>`) — currently only `GetUserPermissionsRequest` for authorization. Do not add new synchronous calls without strong reason; prefer replicating data via integration events (see how Orders keeps a local copy of users).

### Sagas (multi-service workflows)
For workflows spanning 2+ services with compensation or timeouts (e.g., place order → restaurant confirms → delivery assigned → user notified), **suggest a MassTransit state machine saga** instead of chaining integration event handlers. Scaffolding hint already exists (commented out) in `OrdersModule.ConfigureConsumers`:
```csharp
registrationConfigurator
    .AddSagaStateMachine<{Workflow}Saga, {Workflow}State>()
    .RedisRepository(redisConnectionString);
```
Chained handlers are fine for one-hop reactions; use a saga when the flow has 3+ steps, needs rollback/compensation, or its state must be queryable.

### Minimal API Endpoints
```csharp
internal sealed class CreateOrder : IEndpoint {
    public void MapEndpoint(IEndpointRouteBuilder app) {
        app.MapPost("orders", async (Request request, ISender sender) => {
            Result<Guid> result = await sender.Send(new CreateOrderCommand(...));
            return result.Match(Results.Ok, ApiResults.Problem);
        }).RequireAuthorization(Permissions.CreateOrder).WithTags(Tags.Orders);
    }
}
```
Discovered via `AddEndpoints(Presentation.AssemblyReference.Assembly)`, mapped by `app.MapEndpoints()` — no manual registration.

## Identity: Duende IdentityServer (NOT Keycloak)
- Standalone service `FoodDeliveryService.Identity`: Duende IdentityServer + ASP.NET Identity (`ApplicationUser`), in-memory clients/scopes in `Config.cs`, own database
- Issues JWTs; every service and the Gateway validate via JWT Bearer against Duende's discovery endpoint (`Authentication` section in appsettings)
- User registration flow: `POST users/register` (anonymous at gateway) → Users.Api `RegisterUserCommand` → `IIdentityProviderService` → `DuendeIdentityClient` (HTTP, client-credentials token with `users:register` scope via `DuendeAuthDelegatingHandler`) → Identity local API `api/users` → then `User.Create(...)` persists the module-side user and raises `UserRegisteredDomainEvent`
- Authorization: permission-based policies (`PermissionAuthorizationPolicyProvider` + `PermissionRequirement`); permissions come from the Users service via `IPermissionService`

## Observability (always wire up for new services)
- **Tracing**: OpenTelemetry → OTLP exporter → Jaeger (`:4317`, UI `:16686`). `AddInfrastructure` instruments ASP.NET Core, HttpClient, EF Core, Redis, Npgsql, and MassTransit (traces propagate across RabbitMQ). The Gateway adds the `Yarp.ReverseProxy` source. Each service sets its name via `DiagnosticsConfig.ServiceName` (in the API host's `OpenTelemetry/` folder)
- **Logging**: Serilog → Console + Seq (`:5341`, UI `:8081`); `app.UseLogContext()` middleware enriches with correlation info; `UseSerilogRequestLogging()`
- New API hosts must replicate this setup; new external calls must be instrumented

## Data
- One PostgreSQL server, **one database per service**: `fooddeliveryservice_{identity|users|orders|restaurants|notifications}`
- Snake_case naming (`UseSnakeCaseNamingConvention`), default (`public`) schema — custom schemas were removed
- Each module has its own `DbContext` (implements the module's `IUnitOfWork`) + `InsertOutboxMessagesInterceptor`
- Migrations live in `Infrastructure/Database/Migrations` and are auto-applied at startup via `app.ApplyMigrations()`

## Naming Conventions
| Item | Convention | Example |
|------|-----------|---------|
| Domain entity | `sealed class {Name} : Entity` | `Order` |
| Domain event | `{Action}DomainEvent` | `OrderPlacedDomainEvent` |
| Integration event | `{Action}IntegrationEvent` | `OrderPlacedIntegrationEvent` |
| Request/response contract | `{Verb}{Thing}Request` | `GetUserPermissionsRequest` |
| Command | `{Action}{Entity}Command` | `PlaceOrderCommand` |
| Query | `Get{Entity}Query` / `Get{Entities}Query` | `GetOrderQuery` |
| Endpoint class | HTTP verb + resource | `CreateOrder`, `GetOrders` |
| Errors class | `{Entity}Errors` | `OrderErrors.NotFound(id)` |
| App exception | `Common.Application.Exceptions.ApplicationException(requestName, error)` | thrown from event handlers so outbox/inbox retries |
| Docker service | `fooddeliveryservice.{name}` | `fooddeliveryservice.orders.api` |

## Hard Rules (always enforced)
1. **Domain logic MUST live in Domain projects** — never in command handlers
2. **Query handlers MUST use Dapper** via `IDbConnectionFactory` — never EF Core `DbSet<T>` for reads
3. **Never expose domain entities** in API responses — use response record DTOs
4. **Cross-service communication ONLY via the message bus** (integration events or MassTransit request/response) — no HTTP calls between services (only exception: Users → Identity provisioning), no references to another module's Domain/Application/Infrastructure — only its `IntegrationEvents` project
5. **Each service has its own database** — never query another service's tables
6. **Always `IUnitOfWork.SaveChangesAsync()`** — never `DbContext.SaveChanges()`
7. **MassTransit + RabbitMQ** for messaging — not DAPR, not raw RabbitMQ client
8. **Duende IdentityServer** for authentication — not Keycloak
9. **Integration events carry full snapshots** — consumers must never need to call back for data
10. **All external traffic goes through the Gateway** — services are not exposed to clients directly

## Reference Files (in this repo)
| Pattern | Reference File |
|---------|---------------|
| Aggregate + errors + domain events | `src/Modules/Users/...Users.Domain/Users/User.cs` |
| Command handler | `src/Modules/Users/...Users.Application/Users/RegisterUser/RegisterUserCommandHandler.cs` |
| Query handler (Dapper) | `src/Modules/Users/...Users.Application/Users/GetUser/GetUserQueryHandler.cs` |
| Domain event → integration event | `src/Modules/Users/...Users.Application/Users/RegisterUser/UserRegisteredDomainEventHandler.cs` |
| Integration event consumer registration | `src/Modules/Orders/...Orders.Infrastructure/OrdersModule.cs` (`ConfigureConsumers`) |
| MassTransit request/response (RPC) | `src/Modules/Orders/...Orders.Infrastructure/Authorization/PermissionService.cs` + `src/Modules/Users/...Users.Presentation/Users/GetUserPermissionsRequestConsumer.cs` |
| Module registration | `src/Modules/Users/...Users.Infrastructure/UsersModule.cs` |
| API host bootstrap | `src/API/FoodDeliveryService.Orders.Api/Program.cs` |
| YARP config | `src/API/FoodDeliveryService.Gateway/appsettings.Development.json` |
| Entity base + Result<T> | `src/Common/FoodDeliveryService.Common.Domain/` |
| Pipeline behaviors | `src/Common/FoodDeliveryService.Common.Application/Behaviors/` |

## Build & Run
```bash
# from Backend/
dotnet build                                     # builds FoodDeliveryService.Api.slnx
docker-compose up -d                             # all services + PostgreSQL, Redis, RabbitMQ, Seq, Jaeger, Identity, Gateway
dotnet ef migrations add {Name} \
  --project src/Modules/{Module}/FoodDeliveryService.Modules.{Module}.Infrastructure \
  --startup-project src/API/FoodDeliveryService.{Module}.Api
```
Gateway: http://localhost:3000 · Identity: http://localhost:18080 · RabbitMQ UI: http://localhost:15672 · Seq: http://localhost:8081 · Jaeger: http://localhost:16686

## Activity Log
Claude's actions are logged to `Backend/.claude/activity.log` — verify that CLAUDE.md and relevant files are read on each prompt.
