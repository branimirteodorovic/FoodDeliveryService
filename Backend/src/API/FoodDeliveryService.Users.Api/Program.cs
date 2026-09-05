using System.Reflection;
using FoodDeliveryService.Common.Infrastructure;
using FoodDeliveryService.Common.Infrastructure.Configuration;
using FoodDeliveryService.Common.Infrastructure.EventBus;
using FoodDeliveryService.Common.Presentation.Correlation;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Health;
using FoodDeliveryService.Common.Presentation.Security;
using FoodDeliveryService.Modules.Users.Infrastructure;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Serilog;
using StackExchange.Redis;
using FoodDeliveryService.Common.Application;
using FoodDeliveryService.Users.Api.Extensions;
using FoodDeliveryService.Users.Api.OpenTelemetry;
using FoodDeliveryService.Users.Api.Middleware;
using FoodDeliveryService.Users.Api.Seed;

// API host for the Users module (:5400) — reached through the YARP gateway via users/**.
// Owns registration, profiles, roles and permissions, and answers GetUserPermissionsRequest
// (MassTransit request/response) for the other services' authorization.

var builder = WebApplication.CreateBuilder(args);

// Serilog structured logging (Console + Seq sinks, configured in appsettings "Serilog").
builder.Host.UseSerilog((context, loggerConfig) => loggerConfig.ReadFrom.Configuration(context.Configuration));

// Security response headers on every response, and no `Server: Kestrel` on any of them — Feature
// 3.7 Milestone D. The Add half exists separately from app.UseSecurityHeaders() below for one
// reason: KestrelServerOptions.AddServerHeader is read when the server starts and cannot be set from
// the pipeline.
builder.Services.AddSecurityHeaders(builder.Configuration);

// Feature 3.7 Milestone E — configuration fail-fast, the Users half. This host holds the other copy
// of the confidential client secret: it presents it to Identity's api/users when provisioning
// credentials, the one sanctioned service-to-service HTTP call in the platform. appsettings.json
// ships it blank (docs/security.md §3), so a deployment that forgets it used to boot fine and fail
// at the first registration with a 401 nobody could trace back to configuration. Validated
// immediately after Build() below, before migrations and before the administrator seed.
builder.Services.AddRequiredConfiguration(
    builder.Configuration,
    builder.Environment,
    "Duende:AdminUrl",
    "Duende:TokenUrl",
    "Duende:ConfidentialClientId",
    "Duende:ConfidentialClientSecret",
    "Duende:Scope");

// Last-resort exception handling: unhandled exceptions become RFC 7807 ProblemDetails responses.
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// OpenAPI document (/openapi in Development) + Swagger UI for exploring the module's endpoints.
builder.Services.AddOpenApi();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerDocumentation();

// MediatR + FluentValidation for the module's Application assembly (commands, queries, validators).
Assembly[] moduleApplicationAssemblies = [
    FoodDeliveryService.Modules.Users.Application.AssemblyReference.Assembly];

builder.Services.AddApplication(moduleApplicationAssemblies);

// Every service gets its OWN backing stores: a dedicated PostgreSQL database
// (fooddeliveryservice_users), the shared Redis cache and the RabbitMQ broker.
string databaseConnectionString = builder.Configuration.GetConnectionStringOrThrow("Database");
string redisConnectionString = builder.Configuration.GetConnectionStringOrThrow("Cache");
var rabbitMqSettings = new RabbitMqSettings(builder.Configuration.GetConnectionStringOrThrow("Queue"));

// Shared infrastructure stack (see InfrastructureConfiguration): JWT auth (Duende), permission
// authorization, Npgsql + Dapper, Quartz outbox/inbox jobs, Redis caching, MassTransit/RabbitMQ
// messaging (registering this module's consumers), and OpenTelemetry traces + metrics over OTLP.
builder.Services.AddInfrastructure(
    DiagnosticsConfig.ServiceName,
    [UsersModule.ConfigureConsumers()],
    rabbitMqSettings,
    databaseConnectionString,
    redisConnectionString,
    // An unreachable Redis degrades to an in-process cache and an in-process lock in local
    // development only — anywhere else the host keeps the reconnecting Redis connection and lets
    // the health check below report it unhealthy. See docs/caching.md.
    allowInMemoryCacheFallback: builder.Environment.IsDevelopment());

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
// endpoints, outbox/inbox job schedules (see UsersModule).
builder.Services.AddUsersModule(builder.Configuration);

WebApplication app = builder.Build();

// Feature 3.7 Milestone E — run the AddRequiredConfiguration checks before anything with a side
// effect. ValidateOnStart() alone defers them into app.RunAsync(), which is after the migration and
// the administrator seed below.
app.Services.GetRequiredService<IStartupValidator>().Validate();

// EF Core migrations are applied automatically at startup — no manual `dotnet ef database update`.
app.ApplyMigrations();

// Config-driven administrator record seed, aligned to the Identity admin credential via a shared
// IdentityId (idempotent; no-ops when "AdminSeed" is empty). Must run after migrations so the
// roles table is populated.
await AdminSeeder.SeedAdminAsync(app);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// GET /health/live (the process only), GET /health/ready (its dependencies) and the unchanged
// aggregate GET /health — one shared mapping, so all eight hosts expose an identical probe contract.
app.MapHealthProbes();

// One shared middleware for all nine hosts (Common.Presentation/Security): nosniff, DENY framing,
// no referrer, a `default-src 'none'` CSP for the JSON surface, and HSTS only when the request
// actually arrived over HTTPS. It is placed first so that a response short-circuited downstream — an
// authentication challenge, a rate-limit rejection, the exception handler — is stamped too.
app.UseSecurityHeaders();

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
