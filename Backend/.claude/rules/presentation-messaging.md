---
name: presentation-messaging-rules
description: Rules for FoodDeliveryService Presentation layer, IntegrationEvents contracts, and API hosts (endpoints, consumers, YARP, auth)
paths: ["**/Presentation/**/*.cs", "**/*.IntegrationEvents/**/*.cs", "src/API/**/*.cs", "src/API/**/appsettings*.json"]
---

# Presentation, Messaging & API Host Rules

You are editing a **Presentation project, IntegrationEvents contract, or API host**. This is the boundary of a microservice: HTTP in (via YARP gateway), messages in/out (via MassTransit + RabbitMQ).

## Minimal API Endpoints (Presentation)
```csharp
internal sealed class Create{Entity} : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("{module-route}", async (Request request, ISender sender) =>
        {
            Result<Guid> result = await sender.Send(new Create{Entity}Command(...));
            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.{Action}{Entity})   // permission-based policy; omit ONLY for intentionally anonymous endpoints
        .WithTags(Tags.{Module});
    }

    internal sealed class Request { /* input DTO — never a domain entity */ }
}
```
- Discovered via `AddEndpoints(Presentation.AssemblyReference.Assembly)` + `app.MapEndpoints()` — no manual registration
- Routes must fall under the module's YARP path prefix (`orders/**`, `users/**`, `restaurants/**`, `notifications/**`). A genuinely new prefix also needs a route + cluster in `src/API/FoodDeliveryService.Gateway/appsettings.Development.json`
- Auth: JWT from Duende validated at gateway AND service; permissions resolved by `CustomClaimsTransformation` → `IPermissionService` (MassTransit request/response to Users, Redis-cached)

## Consuming Integration Events (inbox pattern)
Two pieces, both required:

**1. Handler in Presentation** (dispatched by `ProcessInboxJob`, idempotent via inbox):
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

**2. Consumer registration in the module's Infrastructure** (`{Module}Module.ConfigureConsumers`):
```csharp
registrationConfigurator.AddConsumer<IntegrationEventConsumer<{Event}IntegrationEvent>>()
    .Endpoint(c => c.InstanceId = instanceId);   // per-service queue — required
```
The generic `IntegrationEventConsumer<T>` (module Infrastructure/Inbox) only writes to `inbox_messages`; the handler above does the actual work later. Never put business logic in a raw MassTransit `IConsumer` — the only exception is request/response consumers (e.g. `GetUserPermissionsRequestConsumer`), which delegate to a service and `RespondAsync`.

## IntegrationEvents Projects (contracts only)
- Only `IntegrationEvent` subclasses and request/response records — no handlers, no logic, no references beyond `Common.Application`
- Full snapshot: include ALL data consumers need; consumers cannot call back across services
- This is the ONLY project other modules may reference

## API Hosts (`src/API/`)
Each host runs exactly one module. Follow `FoodDeliveryService.Orders.Api/Program.cs`:
- `AddApplication([{Module}.Application.AssemblyReference.Assembly])`
- `AddInfrastructure(DiagnosticsConfig.ServiceName, [{Module}Module.ConfigureConsumers], rabbitMqSettings, dbConnString, redisConnString)` — wires auth, MassTransit, OTel traces + metrics (OTLP), Dapper, Redis
- `Add{Module}Module(builder.Configuration)`, health checks, Serilog + Seq, `app.ApplyMigrations()`, `app.UseLogContext()`, `app.MapEndpoints()`
- Health checks follow the probe contract in `docs/health-probe-contract.md`: `.AddLivenessCheck()` plus every dependency (Npgsql, Redis, RabbitMQ, Duende) tagged `HealthCheckTags.Ready`, then one `app.MapHealthProbes()` call for `/health/live` + `/health/ready` + `/health`. An untagged dependency check is invisible to both probes
- Connection string targets the service's OWN database: `fooddeliveryservice_{module}`
- Never expose a service port publicly — clients go through the Gateway (:3000)

## Observability
- Traces and metrics are configured centrally: `AddHostTelemetry` (`Common.Presentation/Telemetry`) is the per-host baseline, and `AddInfrastructure` calls it and adds the module sources (EF Core, Npgsql, Redis, MassTransit) and meters (`Npgsql`, MassTransit, `FoodDeliveryService.Cache`). Traces propagate across RabbitMQ automatically — do not break this by publishing outside `IEventBus`
- Gateway adds the `Yarp.ReverseProxy` source + meter and, like Identity, calls `AddHostTelemetry` directly; new hosts define `DiagnosticsConfig.ServiceName` in an `OpenTelemetry/` folder
- A module's own spans/instruments go on a `{Module}Diagnostics` holder over `AppDiagnostics` (`Common.Infrastructure/Diagnostics`), registered by one `AddModuleDiagnostics({Module}Diagnostics.Name)` call in the host — it wires the activity source **and** the meter. Skipping it doesn't fail: the instrument just records into nothing
