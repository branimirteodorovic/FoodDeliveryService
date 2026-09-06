using FoodDeliveryService.Common.Infrastructure;
using FoodDeliveryService.Common.Infrastructure.Caching;
using FoodDeliveryService.Common.Infrastructure.Configuration;
using FoodDeliveryService.Common.Infrastructure.Diagnostics;
using FoodDeliveryService.Common.Infrastructure.EventBus;
using FoodDeliveryService.Common.Presentation.Correlation;
using FoodDeliveryService.Common.Presentation.Documentation;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Health;
using FoodDeliveryService.Common.Presentation.Security;
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

// Security response headers on every response, and no `Server: Kestrel` on any of them — Feature
// 3.7 Milestone D. The Add half exists separately from app.UseSecurityHeaders() below for one
// reason: KestrelServerOptions.AddServerHeader is read when the server starts and cannot be set from
// the pipeline.
builder.Services.AddSecurityHeaders(builder.Configuration);

// Last-resort exception handling: unhandled exceptions become RFC 7807 ProblemDetails responses.
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Feature 3.7 Milestone G. One shared AddApiDocumentation for all seven module hosts, in
// place of the seven byte-identical SwaggerExtensions copies whose one shared title described
// none of them. It registers a single OpenAPI document — Swashbuckle's SwaggerGen built a
// second one that no host ever served — enriched with this service's identity, the bearer
// scheme, each endpoint's permission and the RFC 7807 failure responses.
builder.Services.AddApiDocumentation(builder.Configuration, ApiDocumentation.RealTime);

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

// Maps every IEndpoint discovered in the module's Presentation assembly — the tracking hub
// self-registers at hubs/tracking; there is no manual route table.
app.MapEndpoints();

// The OpenAPI document and the two UIs over it, at /docs/realtime/{openapi,scalar,swagger}
// — Feature 3.7 Milestone G. Mapped in EVERY environment now rather than only in Development:
// a documented surface that is invisible from the environments other people use documents
// nothing. Outside Development it requires a token. It has to come after UseAuthentication(),
// because that gate reads HttpContext.User.
app.MapApiDocumentation(allowAnonymous: app.Environment.IsDevelopment());

await app.RunAsync();

public partial class Program;
