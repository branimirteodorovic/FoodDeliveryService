# FoodDeliveryService

## Overview
A Food Delivery Service built as **.NET 9 microservices behind a YARP API gateway**.
Follows Domain-Driven Design, CQRS, Railway-Oriented Programming, and Event-Driven patterns.
Namespace prefix is `FoodDeliveryService.*`. All paths below are relative to `Backend/`.

> There is no external reference folder. The reference implementation is the codebase itself — the **Users module** is the most complete example; study it before implementing a feature.

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
1. Client → **Gateway** (`:3000`). YARP validates the JWT (Duende), then routes by path prefix: `orders/**`, `restaurants/**`, `users/**`, `notifications/**` → the matching service. `users/register` is anonymous. Between authentication and routing sits the **edge rate limiter** (`app.UseEdgeRateLimiting()`, `Common.Presentation/RateLimiting`): a global concurrency limit plus a per-client fixed window partitioned by subject (IP when anonymous), sized per route tier so browsing is shed before an order or delivery lifecycle transition is. Counters live in the shared Redis — per-pod buckets would multiply the limit by the replica count. `/health/*` and `hubs/**` are exempt. Rejections are `429` + `Retry-After`. It is **edge-only**: never add a limiter to a module host. `docs/rate-limiting.md`
2. Service validates the JWT again, `CustomClaimsTransformation` resolves permissions via `IPermissionService` (in non-Users services this is a **MassTransit request/response call to Users**, cached in Redis for 5 min).
3. Minimal API endpoint → `ISender.Send(command/query)` → `result.Match(Results.Ok, ApiResults.Problem)`.

New endpoints require a matching YARP route only if they introduce a **new path prefix** — existing `{module}/**` routes cover new endpoints automatically.

## Core Patterns

### CQRS with MediatR
- **Commands** (writes): `sealed record XCommand(...) : ICommand<TResponse>` → `internal sealed class XCommandHandler : ICommandHandler<XCommand, TResponse>` — EF Core repositories + `IUnitOfWork.SaveChangesAsync()`
- **Queries** (reads): `sealed record GetXQuery(...) : IQuery<TResponse>` → `internal sealed class GetXQueryHandler : IQueryHandler<GetXQuery, TResponse>` — **Dapper via `IDbConnectionFactory`**, never EF Core
- All return `Result<T>` — never throw for business failures
- Pipeline behaviors: ExceptionHandling → RequestMetrics → RequestLogging → Validation (FluentValidation) → QueryCaching

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

### Distributed Locking (concurrent writes to contended state)
`IDistributedLock` (`Common.Application/Locking`; Redis `SET NX PX` + token-checked Lua release) is the cross-process `lock`. A C# `lock` protects nothing here: services run as multiple replicas, and Quartz's `[DisallowConcurrentExecution]` is per-scheduler — two pods tick the same job at the same instant.

Take it when a write is **check-then-act on state another caller can change** (read a status → decide → write) and losing the race double-books a scarce resource. **No aggregate carries an optimistic concurrency token**, so the database will *not* reject the second write — nothing else is protecting you.

```csharp
// Acquire BEFORE the read — the check-then-act begins at the read, so a lock
// taken after it still lets both callers act on the same stale snapshot.
await using IAsyncDisposable? handle = await distributedLock.TryAcquireAsync(resource, ttl, ct);
if (handle is null) return Result.Failure({Entity}Errors.SomethingInProgress);
```
- Keys + TTL live in **one** shared static per module (`DeliveryLocks`) so the read side and write side can't drift onto different names.
- TTL comfortably exceeds the critical section, far short of the business window it guards (assignment: 5 s vs a 30 s offer).
- A lost acquisition must land somewhere a retry actually exists. Returning `Result.Success()` strands the entity if nothing re-drives it — note `ProcessInboxJob` does **not** retry: it records the error and marks the message processed.
- The lock *complements* the aggregate guard (`driver.Reserve()`), never replaces it. It's advisory — a new write path that skips it reopens the race.

Reference: `DeliveryAssignmentService.OfferNextAsync` + `AcceptDeliveryOfferCommandHandler`.

### Caching (Redis)
A query opts into caching by implementing `ICachedQuery<TResponse>` (cache key + TTL); `QueryCachingBehavior` wraps the handler in `ICacheService.GetOrCreateAsync`, so handlers stay pure Dapper and failures are never cached. Keys come from a module's convention class (`RestaurantCacheKeys`) built on `CacheKeys.Create` — never concatenated at a call site. **Invalidation is a `RemoveAsync` inline in the command handler, right after `SaveChangesAsync`** — not a domain-event handler, whose outbox lag both delays freshness and, for keys an event handler reads back, publishes stale snapshots.

One Redis instance per environment carries the cache, the `IDistributedLock`, Delivery's live driver GEO store and the SignalR backplane, so it is an availability dependency, not a latency optimization. All of them share the multiplexer built by `AddInfrastructure` from `RedisConnectionOptions.Create` (`abortConnect=false` + exponential reconnect; anything else comes from the `ConnectionStrings:Cache` string, which is all that changes to run on Azure Cache for Redis). An unreachable Redis degrades to an in-process cache **and an in-process lock** in Development only — hosts pass `allowInMemoryCacheFallback: builder.Environment.IsDevelopment()`. Keys, TTLs, the invalidation model and the Azure smoke check: `docs/caching.md`.

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
- **Traces + metrics**: one OpenTelemetry baseline — `AddHostTelemetry(serviceName)` (`Common.Presentation/Telemetry`) — gives every host the resource, ASP.NET Core + HttpClient tracing, ASP.NET Core + HttpClient + runtime + process metrics and the OTLP exporter on both pillars. Module hosts get it through `AddInfrastructure`, which adds EF Core, Redis, Npgsql and MassTransit sources plus the `Npgsql`, MassTransit and `FoodDeliveryService.Cache` meters; the **Gateway and Identity call it directly** (they take no `Common.Infrastructure` dependency) and the Gateway adds the `Yarp.ReverseProxy` source and meter. Each service sets its name via `DiagnosticsConfig.ServiceName` (in the API host's `OpenTelemetry/` folder)
- **Metrics backend**: every host exports to the **OpenTelemetry Collector** (`OTEL_EXPORTER_OTLP_ENDPOINT` → `http://fooddeliveryservice.otel-collector:4317` — never straight at Jaeger, which drops metrics), which fans traces to Jaeger and metrics to Prometheus (`:9090`), graphed by provisioned Grafana dashboards (`:3100`) and alerted on by Prometheus rules. Config and dashboards-as-code live in `docker/{otel-collector,prometheus,grafana,blackbox}`; a blackbox exporter probes every host's `/health/live` + `/health/ready` so "service down" is a real signal. Instrument names are renamed on the way to Prometheus (`orders.placed` → `orders_placed_total`, `app.request.duration` → `app_request_duration_seconds_*`), and `ObservabilityAssetTests` fails the build if a dashboard or alert names a metric nothing emits. Everything: `docs/observability-backend.md`
- **Custom spans and metrics**: declare a `{Module}Diagnostics` static holder over an `AppDiagnostics` (`Common.Application/Diagnostics` — the Application layer, because the handlers that record business metrics can't reference `Common.Infrastructure`) — one name carrying both an `ActivitySource` and a `Meter` — and register it with a single `builder.Services.AddModuleDiagnostics({Module}Diagnostics.Name)` in the host (that extension lives in `Common.Infrastructure/Diagnostics`), which does `AddSource` **and** `AddMeter`. Reference: `OrdersDiagnostics`, `DeliveryDiagnostics`, `RealTimeDiagnostics`. An unregistered source or meter never errors, it just silently records into nothing. Keep tag cardinality bounded — enum values and type names, never ids or user input
- **Application RED**: `RequestMetricsBehavior` records `app.requests` / `app.request.duration` / `app.request.failures` for every command and query, tagged by request type and by the outcome derived from the returned `Result` (`ApplicationDiagnostics`). Handlers stay pure — never hand-record a request metric. It is registered **second**, outside `QueryCachingBehavior`, so a cache hit is still measured
- **Business metrics** are emitted where the state change is already owned — a counter next to an existing domain-event handler, recorded **last** so an outbox retry of a failed handler doesn't double-count. Reference: `OrdersDiagnostics` (`orders.placed`, `orders.state_transition`), `DeliveryAssignmentDiagnostics` (`delivery.assignment.outcome`/`.duration`)
- **Logging**: Serilog → Console + Seq (`:5341`, UI `:8081`); `UseSerilogRequestLogging()`
- **Correlation**: one shared `app.UseRequestCorrelation()` (`Common.Presentation/Correlation`) on all eight hosts, placed before `UseSerilogRequestLogging()`. It resolves `X-Correlation-Id` — inbound value preserved (the Gateway stamps it and YARP forwards it), otherwise **defaulted to the W3C trace id** so one string finds both the Seq logs and the Jaeger trace — echoes it on the response, and pushes `TraceId` + `SpanId` + `ServiceName` + the route's business id (`orders/{id}` → `OrderId`) into the Serilog `LogContext`. Never mint a competing id scheme, and never re-implement this per host: seven near-identical copies is what it replaced
- **Correlation across the outbox/inbox boundary**: the same id survives the two database handoffs, so it covers the asynchronous legs too. The middleware also pushes the id + the request's `traceparent` into `CorrelationContext` (ambient, injected, `Common.Presentation/Correlation`); `InsertOutboxMessagesInterceptor` and `IntegrationEventConsumer` stamp them onto the nullable `correlation_id` / `trace_parent` **columns** of `outbox_messages` / `inbox_messages`; a MassTransit publish/consume filter pair carries the id over the broker on a header (not the envelope `CorrelationId`, which is a `Guid`); and every `ProcessOutboxJob`/`ProcessInboxJob` opens a `MessageDispatchScope` per message, which restores the id into the `LogContext`, adds the event's own business ids, and starts a dispatch span **linked** to the originating trace (a batch belongs to N requests, so it is deliberately a new root, not a child). Read the columns rather than re-deriving anything, and if a job is ever added, it opens that scope — do not write the logic a twelfth time
- **Health probes**: every host maps `/health/live` (process only), `/health/ready` (dependencies) and `/health` (aggregate) via the shared `app.MapHealthProbes()`; dependency checks are tagged `HealthCheckTags.Ready` and `.AddLivenessCheck()` adds the `live` self check. Contract: `docs/health-probe-contract.md`
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
| Distributed lock (check-then-act writes) | `src/Modules/Delivery/...Delivery.Infrastructure/Assignment/DeliveryAssignmentService.cs` + `.../Application/Abstractions/Assignment/DeliveryLocks.cs` |
| Cached query + inline invalidation | `src/Modules/Restaurants/...Restaurants.Application/Restaurants/GetMenu/GetMenuQuery.cs` + `.../UpdateMenuItem/UpdateMenuItemCommandHandler.cs` (docs: `docs/caching.md`) |
| Domain unit tests | `src/Modules/Restaurants/...Restaurants.UnitTests/` (skill: `/write-unit-tests`) |
| Full-stack integration tests | `src/Modules/Restaurants/...Restaurants.IntegrationTests/` (skill: `/write-integration-tests`) |

## Testing
- **Unit tests** (`{Module}.UnitTests`, references Domain only): xUnit v3 + AwesomeAssertions + Bogus. Cover an aggregate's factory, business methods, invariants, and the domain events they raise (or must NOT raise on a no-op). No DI/DB/HTTP. Use `/write-unit-tests {Module} {Aggregate}`.
- **Integration tests** (`{Module}.IntegrationTests`): drive the real HTTP endpoint through the full pipeline against ephemeral Postgres/Redis/RabbitMQ Testcontainers, with real Duende JWTs (needs `fooddeliveryservice.identity` up on `:18080`). Host other modules' APIs in-process to assert cross-service event propagation. Use `/write-integration-tests {Module} {Feature}`.

## Build & Run
```bash
# from Backend/
dotnet build                                     # builds FoodDeliveryService.Api.slnx
docker-compose up -d                             # all services + PostgreSQL, Redis, RabbitMQ, Seq, Jaeger, OTel Collector, Prometheus, Grafana, blackbox, Identity, Gateway
dotnet ef migrations add {Name} \
  --project src/Modules/{Module}/FoodDeliveryService.Modules.{Module}.Infrastructure \
  --startup-project src/API/FoodDeliveryService.{Module}.Api
```
Gateway: http://localhost:3000 · Identity: http://localhost:18080 · RabbitMQ UI: http://localhost:15672 · Seq: http://localhost:8081 · Jaeger: http://localhost:16686 · Grafana: http://localhost:3100 · Prometheus: http://localhost:9090

## Activity Log
Claude's actions are logged to `Backend/.claude/activity.log` — verify that CLAUDE.md and relevant files are read on each prompt.
