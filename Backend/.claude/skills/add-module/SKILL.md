---
description: Scaffold a complete new microservice (module + API host + gateway route) for FoodDeliveryService. Use when adding a new bounded context.
disable-model-invocation: true
argument-hint: [ModuleName]
---

# Add Module / Microservice: $ARGUMENTS

A new module means a **new microservice**: 5 module projects + a dedicated API host + Docker wiring + a YARP route + its own database.

**Fastest path: copy the Restaurants service** (simplest complete example) and rename. Reference files:
- Module: `src/Modules/Restaurants/` (all 5 projects)
- API host: `src/API/FoodDeliveryService.Restaurants.Api/` (Program.cs, Dockerfile, Extensions/, Middleware/, OpenTelemetry/, appsettings)
- Richest module (identity, RPC consumer): `src/Modules/Users/`

## 1. Module Projects

| Project | References |
|---------|-----------|
| `FoodDeliveryService.Modules.$ARGUMENTS.Domain` | `Common.Domain` only |
| `FoodDeliveryService.Modules.$ARGUMENTS.Application` | Common.Application, .Domain |
| `FoodDeliveryService.Modules.$ARGUMENTS.IntegrationEvents` | Common.Application only |
| `FoodDeliveryService.Modules.$ARGUMENTS.Infrastructure` | Common.Infrastructure, .Application, .Presentation, .IntegrationEvents |
| `FoodDeliveryService.Modules.$ARGUMENTS.Presentation` | Common.Presentation, .Application, .IntegrationEvents |

Required files (copy from Restaurants and rename):
- Domain: `AssemblyReference.cs`, aggregate + `I{Entity}Repository` + `{Entity}Errors` + domain events under `{Entity}s/`
- Application: `AssemblyReference.cs`, `Abstractions/Data/IUnitOfWork.cs`, `Abstractions/Authentication/I$ARGUMENTSContext.cs`
- Infrastructure: `Database/$ARGUMENTSDbContext.cs`, `Database/Migrations/`, `Inbox/` (ProcessInboxJob, ConfigureProcessInboxJob, InboxOptions, IntegrationEventConsumer, IdempotentIntegrationEventHandler), `Outbox/` (same shape), `Authentication/$ARGUMENTSContext.cs`, `Authorization/PermissionService.cs`, `$ARGUMENTSModule.cs`
- Presentation: `AssemblyReference.cs` (+ endpoints, `IIntegrationEventHandler` implementations)
- IntegrationEvents: event contracts

**DbContext** (no custom schema — snake_case in the service's own DB):
```csharp
public sealed class $ARGUMENTSDbContext(DbContextOptions<$ARGUMENTSDbContext> options)
    : DbContext(options), IUnitOfWork
{
    internal DbSet<OutboxMessage> OutboxMessages { get; set; }
    internal DbSet<InboxMessage> InboxMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
    }
}
```

**$ARGUMENTSModule.cs** — follow `OrdersModule.cs` exactly:
- `Add$ARGUMENTSModule(IServiceCollection, IConfiguration)`: `AddDomainEventHandlers()` + `AddIntegrationEventHandlers()` + `AddInfrastructure(configuration)` + `AddEndpoints(Presentation.AssemblyReference.Assembly)`
- `ConfigureConsumers(IRegistrationConfigurator registrationConfigurator, string instanceId, string redisConnectionString)`: one `AddConsumer<IntegrationEventConsumer<TEvent>>().Endpoint(c => c.InstanceId = instanceId)` per consumed event
- `AddInfrastructure`: DbContext (`UseNpgsql` + `UseSnakeCaseNamingConvention` + `AddInterceptors(InsertOutboxMessagesInterceptor)`), `IUnitOfWork` from DbContext, repositories, `IPermissionService`, `MessageProcessor:Outbox|Inbox` options + `ConfigureProcessOutboxJob`/`ConfigureProcessInboxJob`

## 2. API Host: `src/API/FoodDeliveryService.$ARGUMENTS.Api`

Copy `FoodDeliveryService.Restaurants.Api` and rename. It must contain:
- `Program.cs`: Serilog, `AddApplication([Modules.$ARGUMENTS.Application.AssemblyReference.Assembly])`, `AddInfrastructure(DiagnosticsConfig.ServiceName, [$ARGUMENTSModule.ConfigureConsumers], rabbitMqSettings, dbConnString, redisConnString)`, health checks (Npgsql, Redis, RabbitMQ, Duende), `Add$ARGUMENTSModule`, `app.ApplyMigrations()`, `app.UseRequestCorrelation()`, `app.UseAuthentication()/UseAuthorization()`, `app.MapEndpoints()`
- `OpenTelemetry/DiagnosticsConfig.cs` with `ServiceName = "FoodDeliveryService.$ARGUMENTS.Api"` (this is the service name in Jaeger)
- `Dockerfile` (context is repo root `Backend/`)
- csproj: reference `FoodDeliveryService.Modules.$ARGUMENTS.Infrastructure` only; same packages as Orders.Api
- `appsettings.Development.json`: copy from Orders.Api and change:
  - `ConnectionStrings:Database` → `Database=fooddeliveryservice_{arguments_lowercase}` (**own database — never share**)
  - `Serilog:Properties:Application` → `FoodDeliveryService.$ARGUMENTS.Api`
  - Keep `Authentication` (Duende), `Duende:HealthUrl`, `OTEL_EXPORTER_OTLP_ENDPOINT`, `MessageProcessor` sections

Add all 6 projects to `FoodDeliveryService.Api.slnx`.

## 3. Docker Compose (`docker-compose.yml` + override)
```yaml
  fooddeliveryservice.{arguments_lowercase}.api:
    image: ${DOCKER_REGISTRY-}fooddeliveryservice{arguments_lowercase}api
    container_name: FoodDeliveryService.$ARGUMENTS.Api
    build:
      context: .
      dockerfile: src/API/FoodDeliveryService.$ARGUMENTS.Api/Dockerfile
    ports:
      - 5X00:8080   # next free port pair: Notifications 5100, Orders 5200, Restaurants 5300, Users 5400
      - 5X01:8081
```
Mirror whatever the override file adds for the other APIs (env vars, depends_on).

## 4. YARP Gateway Route
In `src/API/FoodDeliveryService.Gateway/appsettings.Development.json` add a route + cluster:
```json
"fooddeliveryservice-{arguments_lowercase}-route1": {
  "ClusterId": "fooddeliveryservice-{arguments_lowercase}-cluster",
  "AuthorizationPolicy": "default",
  "Match": { "Path": "{arguments_lowercase}/{**catch-all}" },
  "Transforms": [ { "PathPattern": "{arguments_lowercase}/{**catch-all}" } ]
}
```
```json
"fooddeliveryservice-{arguments_lowercase}-cluster": {
  "Destinations": {
    "destination1": { "Address": "http://fooddeliveryservice.{arguments_lowercase}.api:8080" }
  }
}
```

## 5. Initial Migration
```bash
dotnet ef migrations add Create_Database \
  --project src/Modules/$ARGUMENTS/FoodDeliveryService.Modules.$ARGUMENTS.Infrastructure \
  --startup-project src/API/FoodDeliveryService.$ARGUMENTS.Api
```
Migrations auto-apply at startup via `app.ApplyMigrations()`.

## Checklist
- [ ] `dotnet build` passes
- [ ] Service has its own database name in the connection string
- [ ] `ConfigureConsumers` registered in Program.cs (`AddInfrastructure` second argument)
- [ ] YARP route + cluster added; service reachable through the gateway, not directly
- [ ] `DiagnosticsConfig.ServiceName` set — service appears in Jaeger; logs appear in Seq
- [ ] Consumed events from other services wired per `/add-integration-event`
