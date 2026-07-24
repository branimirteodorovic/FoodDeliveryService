using FoodDeliveryService.Common.Infrastructure;
using FoodDeliveryService.Common.Infrastructure.Configuration;
using FoodDeliveryService.Common.Infrastructure.EventBus;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Modules.RealTime.Infrastructure;
using FoodDeliveryService.RealTime.Api.Extensions;
using FoodDeliveryService.RealTime.Api.Middleware;
using FoodDeliveryService.RealTime.Api.OpenTelemetry;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Trace;
using RabbitMQ.Client;
using Serilog;

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
// OpenTelemetry tracing to Jaeger.
builder.Services.AddInfrastructure(
    DiagnosticsConfig.ServiceName,
    [RealTimeModule.ConfigureConsumers],
    rabbitMqSettings,
    databaseConnectionString,
    redisConnectionString);

// Register the RealTime-specific tracing source (the Milestone C location-forward span) alongside
// the instrumentation AddInfrastructure already wired up.
builder.Services.ConfigureOpenTelemetryTracerProvider(tracing =>
    tracing.AddSource(FoodDeliveryService.Modules.RealTime.Infrastructure.RealTime.RealTimeDiagnostics.SourceName));

// SignalR with a Redis backplane so scale-out across multiple RealTime instances works: any
// instance can broadcast to a connection held by any other instance.
builder.Services.AddSignalR().AddStackExchangeRedis(redisConnectionString);

// The browser WebSocket handshake can't set an Authorization header, so SignalR sends the JWT as
// the access_token query-string parameter; this hook feeds it to JwtBearer for hubs/* paths only.
builder.Services.AddRealTimeHubAuthentication();

Uri duendeHealthUrl = builder.Configuration.GetDuendeHealthUrl();

// Liveness probes for every external dependency this service uses — PostgreSQL (Npgsql, from
// Milestone D), Redis (cache + backplane), RabbitMQ (reuses the raw IConnection from
// AddInfrastructure) and the Duende IdentityServer /health endpoint.
builder.Services.AddHealthChecks()
    .AddNpgSql(databaseConnectionString)
    .AddRedis(redisConnectionString)
    .AddRabbitMQ(sp => sp.GetRequiredService<IConnection>())
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

// GET /health — aggregated dependency status rendered as JSON (HealthChecks.UI.Client format).
app.MapHealthChecks("health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

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
