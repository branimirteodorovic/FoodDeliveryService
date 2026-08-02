using System.Reflection;
using FoodDeliveryService.Common.Application;
using FoodDeliveryService.Common.Infrastructure;
using FoodDeliveryService.Common.Infrastructure.Configuration;
using FoodDeliveryService.Common.Infrastructure.Diagnostics;
using FoodDeliveryService.Common.Infrastructure.EventBus;
using FoodDeliveryService.Common.Presentation.Correlation;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Health;
using FoodDeliveryService.Delivery.Api.Extensions;
using FoodDeliveryService.Delivery.Api.Middleware;
using FoodDeliveryService.Delivery.Api.OpenTelemetry;
using FoodDeliveryService.Modules.Delivery.Infrastructure;
using RabbitMQ.Client;
using Serilog;
using StackExchange.Redis;

// API host for the Delivery module (:5500) — reached through the YARP gateway via delivery/**.
// Owns driver profiles and (from Milestone E) the delivery leg of every order. Onboards drivers
// via the ProvisionUserRequest RPC to Users and keeps driver name snapshots in sync by consuming
// UserProfileUpdated integration events.

var builder = WebApplication.CreateBuilder(args);

// Serilog structured logging (Console + Seq sinks, configured in appsettings "Serilog").
builder.Host.UseSerilog((context, loggerConfig) => loggerConfig.ReadFrom.Configuration(context.Configuration));

// Last-resort exception handling: unhandled exceptions become RFC 7807 ProblemDetails responses.
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// OpenAPI document (/openapi in Development) + Swagger UI for exploring the module's endpoints.
builder.Services.AddOpenApi();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerDocumentation();

// MediatR + FluentValidation for the module's Application assembly (commands, queries, validators).
Assembly[] moduleApplicationAssemblies = [
    FoodDeliveryService.Modules.Delivery.Application.AssemblyReference.Assembly];

builder.Services.AddApplication(moduleApplicationAssemblies);

// Every service gets its OWN backing stores: a dedicated PostgreSQL database
// (fooddeliveryservice_delivery), the shared Redis cache and the RabbitMQ broker.
string databaseConnectionString = builder.Configuration.GetConnectionStringOrThrow("Database");
string redisConnectionString = builder.Configuration.GetConnectionStringOrThrow("Cache");
var rabbitMqSettings = new RabbitMqSettings(builder.Configuration.GetConnectionStringOrThrow("Queue"));

// Shared infrastructure stack (see InfrastructureConfiguration): JWT auth (Duende), permission
// authorization, Npgsql + Dapper, Quartz outbox/inbox jobs, Redis caching, MassTransit/RabbitMQ
// messaging (registering this module's consumers), and OpenTelemetry traces + metrics over OTLP.
builder.Services.AddInfrastructure(
    DiagnosticsConfig.ServiceName,
    [DeliveryModule.ConfigureConsumers()],
    rabbitMqSettings,
    databaseConnectionString,
    redisConnectionString,
    // An unreachable Redis degrades to an in-process cache and an in-process lock in local
    // development only — anywhere else the host keeps the reconnecting Redis connection and lets
    // the health check below report it unhealthy. See docs/caching.md.
    allowInMemoryCacheFallback: builder.Environment.IsDevelopment());

// Registers the module's own activity source (the "find nearest available driver" geo span) AND its
// meter under one name, alongside the instrumentation AddInfrastructure already wired up. One call
// for both pillars, so a Delivery instrument can't ship unregistered and silently uncollected.
builder.Services.AddModuleDiagnostics(
    FoodDeliveryService.Modules.Delivery.Infrastructure.Locations.DeliveryDiagnostics.Name);

Uri duendeHealthUrl = builder.Configuration.GetDuendeHealthUrl();

// AspNetCore.HealthChecks.* packages: the two probe check sets. Redis and RabbitMQ probe the very
// connections AddInfrastructure registered, so they report on what the app actually uses — TLS and
// reconnect policy included — rather than on a second connection opened just for the check.
builder.Services.AddHealthChecks()
    // The dependency-free "self" check behind GET /health/live: reaching it at all is the signal.
    // Nothing an outage elsewhere can break may join it — a liveness failure restarts the container,
    // and restarting a pod does not bring PostgreSQL back.
    .AddLivenessCheck()
    // Everything below is the readiness set behind GET /health/ready — tagged so a dependency outage
    // pulls the pod out of rotation while leaving it running. MassTransit registers its own
    // "masstransit-bus" check, already tagged ready. See docs/health-probe-contract.md.
    .AddNpgSql(databaseConnectionString, tags: [HealthCheckTags.Ready])
    .AddRedis(sp => sp.GetRequiredService<IConnectionMultiplexer>(), tags: [HealthCheckTags.Ready])
    .AddRabbitMQ(sp => sp.GetRequiredService<IConnection>(), tags: [HealthCheckTags.Ready])
    // Tags itself ready: an Identity outage deliberately takes every module host unready at once,
    // because a service that cannot resolve permissions cannot serve authenticated traffic.
    .AddDuende(duendeHealthUrl);

// Module-specific registrations: DbContext, repositories, domain/integration event handlers,
// endpoints, outbox/inbox job schedules (see DeliveryModule).
builder.Services.AddDeliveryModule(builder.Configuration);

WebApplication app = builder.Build();

// EF Core migrations are applied automatically at startup — no manual `dotnet ef database update`.
app.ApplyMigrations();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// GET /health/live (the process only), GET /health/ready (its dependencies) and the unchanged
// aggregate GET /health — one shared mapping, so all eight hosts expose an identical probe contract.
app.MapHealthProbes();

// One shared middleware (Common.Presentation/Correlation) for the whole platform: it preserves the
// X-Correlation-Id the Gateway stamped — or mints one from the trace id for a call that reached this
// host directly — echoes it on the response, and pushes TraceId + SpanId + ServiceName + any business
// id on the route into the Serilog LogContext, so a Seq line links to its Jaeger span and every line
// about one order is a single query away.
app.UseRequestCorrelation();

app.UseSerilogRequestLogging();

app.UseExceptionHandler();

app.UseAuthentication();

app.UseAuthorization();

// Maps every IEndpoint implementation discovered in the module's Presentation assembly —
// endpoints self-register; there is no manual route table.
app.MapEndpoints();

await app.RunAsync();

public partial class Program;
