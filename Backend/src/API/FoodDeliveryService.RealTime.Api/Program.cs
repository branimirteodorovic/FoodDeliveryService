using FoodDeliveryService.Common.Infrastructure;
using FoodDeliveryService.Common.Infrastructure.Caching;
using FoodDeliveryService.Common.Infrastructure.Configuration;
using FoodDeliveryService.Common.Infrastructure.Diagnostics;
using FoodDeliveryService.Common.Infrastructure.EventBus;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Health;
using FoodDeliveryService.Modules.RealTime.Infrastructure;
using FoodDeliveryService.RealTime.Api.Extensions;
using FoodDeliveryService.RealTime.Api.Middleware;
using FoodDeliveryService.RealTime.Api.OpenTelemetry;
using RabbitMQ.Client;
using Serilog;
using StackExchange.Redis;

// API host for the Real-Time service (:5600) — reached through the YARP gateway via hubs/**.
// Holds authenticated SignalR connections and fans out ephemeral order-status and driver-location
// frames to the right groups. Status updates ride direct MassTransit consumers (Milestone B), not
// the durable inbox — a deliberate, documented departure justified by the socket being best-effort.
// From Milestone D it owns its first (and only) database — a minimal RestaurantManager replica,
// consumed durably via the inbox because that mapping (unlike a transient frame) must survive a
// cold start.

var builder = WebApplication.CreateBuilder(args);

// Serilog structured logging (Console + Seq sinks, configured in appsettings "Serilog").
builder.Host.UseSerilog((context, loggerConfig) => loggerConfig.ReadFrom.Configuration(context.Configuration));

// Last-resort exception handling: unhandled exceptions become RFC 7807 ProblemDetails responses.
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// OpenAPI document (/openapi in Development) + Swagger UI. The hub is the service's real surface,
// but the host keeps the same bootstrap as the other services for consistency.
builder.Services.AddOpenApi();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerDocumentation();

// From Milestone D this service has its own database (the RestaurantManager replica) alongside the
// shared Redis cache (also the SignalR backplane) and the RabbitMQ broker (event consumption + the
// permission RPC).
string databaseConnectionString = builder.Configuration.GetConnectionStringOrThrow("Database");
string redisConnectionString = builder.Configuration.GetConnectionStringOrThrow("Cache");
var rabbitMqSettings = new RabbitMqSettings(builder.Configuration.GetConnectionStringOrThrow("Queue"));

// Full infrastructure stack: JWT auth (Duende), permission authorization, Npgsql + Dapper, Quartz
// (outbox/inbox jobs — this module only schedules an inbox job, see RealTimeModule), Redis caching,
// MassTransit/RabbitMQ messaging (registering this module's request client + consumers), and
// OpenTelemetry traces + metrics over OTLP.
builder.Services.AddInfrastructure(
    DiagnosticsConfig.ServiceName,
    [RealTimeModule.ConfigureConsumers],
    rabbitMqSettings,
    databaseConnectionString,
    redisConnectionString,
    // An unreachable Redis degrades to an in-process cache and an in-process lock in local
    // development only — anywhere else the host keeps the reconnecting Redis connection and lets
    // the health check below report it unhealthy. See docs/caching.md.
    allowInMemoryCacheFallback: builder.Environment.IsDevelopment());

// Registers the module's own activity source (the location-forward span) AND its meter under one
// name, alongside the instrumentation AddInfrastructure already wired up. One call for both pillars,
// so a Real-Time instrument can't ship unregistered and silently uncollected.
builder.Services.AddModuleDiagnostics(
    FoodDeliveryService.Modules.RealTime.Infrastructure.RealTime.RealTimeDiagnostics.Name);

// SignalR with a Redis backplane so scale-out across multiple RealTime instances works: any
// instance can broadcast to a connection held by any other instance. It builds its own connection
// (SignalR owns the backplane's subscriptions), so it is given the same hardened options as the
// cache — otherwise the backplane would be the one connection that ignores TLS and the reconnect
// policy on an Azure Cache for Redis endpoint.
builder.Services.AddSignalR().AddStackExchangeRedis(options =>
    options.Configuration = RedisConnectionOptions.Create(redisConnectionString, DiagnosticsConfig.ServiceName));

// The browser WebSocket handshake can't set an Authorization header, so SignalR sends the JWT as
// the access_token query-string parameter; this hook feeds it to JwtBearer for hubs/* paths only.
builder.Services.AddRealTimeHubAuthentication();

Uri duendeHealthUrl = builder.Configuration.GetDuendeHealthUrl();

// AspNetCore.HealthChecks.* packages: the two probe check sets. Redis (the cache multiplexer
// registered by AddInfrastructure; the SignalR backplane's own connection shares its configuration)
// and RabbitMQ (the raw IConnection from AddInfrastructure) probe the very connections the app uses,
// rather than a second connection opened just for the check.
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

// Module-specific registrations: the tracking hub endpoint + the permission service used by
// CustomClaimsTransformation on the handshake (see RealTimeModule).
builder.Services.AddRealTimeModule(builder.Configuration);

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

// Pushes trace/correlation ids into the Serilog LogContext so Seq logs link to Jaeger traces.
app.UseLogContext();

app.UseSerilogRequestLogging();

app.UseExceptionHandler();

app.UseAuthentication();

app.UseAuthorization();

// Maps every IEndpoint discovered in the module's Presentation assembly — the tracking hub
// self-registers at hubs/tracking; there is no manual route table.
app.MapEndpoints();

await app.RunAsync();

public partial class Program;
