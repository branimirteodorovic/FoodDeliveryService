using System.Reflection;
using FoodDeliveryService.Common.Application;
using FoodDeliveryService.Common.Infrastructure;
using FoodDeliveryService.Common.Infrastructure.Configuration;
using FoodDeliveryService.Common.Infrastructure.EventBus;
using FoodDeliveryService.Common.Presentation.Correlation;
using FoodDeliveryService.Common.Presentation.Documentation;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Health;
using FoodDeliveryService.Common.Presentation.Security;
using FoodDeliveryService.Payments.Api.Extensions;
using FoodDeliveryService.Payments.Api.Middleware;
using FoodDeliveryService.Payments.Api.OpenTelemetry;
using FoodDeliveryService.Modules.Payments.Infrastructure;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Serilog;
using StackExchange.Redis;

// API host for the Payments module (:5800) — reached through the YARP gateway via payments/**.
// Tenth host, eighth module. It will own card payments against Stripe: an authorization when an
// order is placed, a capture when the restaurant accepts it, a release when it is rejected or
// cancelled, and the refund that makes Support's approved refund request move money. This milestone
// ships the service and nothing it does — the Stripe seam, the aggregates and the endpoints arrive
// in the milestones after it.
//
// Payments is its own service rather than a slice of Orders so that the Stripe SDK, the API keys
// and the anonymous webhook ingress stay off the core order path. It calls no other service
// synchronously except the permissions RPC every module host makes.

var builder = WebApplication.CreateBuilder(args);

// Serilog structured logging (Console + Seq sinks, configured in appsettings "Serilog").
builder.Host.UseSerilog((context, loggerConfig) => loggerConfig.ReadFrom.Configuration(context.Configuration));

// Security response headers on every response, and no `Server: Kestrel` on any of them — Feature
// 3.7 Milestone D. The Add half exists separately from app.UseSecurityHeaders() below for one
// reason: KestrelServerOptions.AddServerHeader is read when the server starts and cannot be set from
// the pipeline.
builder.Services.AddSecurityHeaders(builder.Configuration);

// Feature 3.8 Milestone C — configuration fail-fast for the Stripe credentials, the same mechanism
// Feature 3.7 Milestone E gave Identity and Users. Both keys ship blank in appsettings.json and are
// supplied by the environment, so without this a deployment that forgets one boots perfectly happily
// and fails at the first order with a 401 from Stripe that points at nothing.
//
// It skips Development by design, and that carve-out is load-bearing here: a `docker-compose up`
// without Stripe user secrets, and every integration test that boots this host against a fake
// gateway, must still start. The checks that DO run everywhere — that a key is a test key and not a
// live one — live in StripeOptionsValidator instead.
builder.Services.AddRequiredConfiguration(
    builder.Configuration,
    builder.Environment,
    "Stripe:SecretKey",
    "Stripe:WebhookSecret");

// Last-resort exception handling: unhandled exceptions become RFC 7807 ProblemDetails responses.
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Feature 3.7 Milestone G. One shared AddApiDocumentation for every module host, in
// place of the byte-identical SwaggerExtensions copies whose one shared title described
// none of them. It registers a single OpenAPI document — Swashbuckle's SwaggerGen built a
// second one that no host ever served — enriched with this service's identity, the bearer
// scheme, each endpoint's permission and the RFC 7807 failure responses.
builder.Services.AddApiDocumentation(builder.Configuration, ApiDocumentation.Payments);

// MediatR + FluentValidation for the module's Application assembly (commands, queries, validators).
Assembly[] moduleApplicationAssemblies = [
    FoodDeliveryService.Modules.Payments.Application.AssemblyReference.Assembly];

builder.Services.AddApplication(moduleApplicationAssemblies);

// Every service gets its OWN backing stores: a dedicated PostgreSQL database
// (fooddeliveryservice_payments), the shared Redis cache and the RabbitMQ broker.
string databaseConnectionString = builder.Configuration.GetConnectionStringOrThrow("Database");
string redisConnectionString = builder.Configuration.GetConnectionStringOrThrow("Cache");
var rabbitMqSettings = new RabbitMqSettings(builder.Configuration.GetConnectionStringOrThrow("Queue"));

// Shared infrastructure stack (see InfrastructureConfiguration): JWT auth (Duende), permission
// authorization, Npgsql + Dapper, Quartz outbox/inbox jobs, Redis caching, MassTransit/RabbitMQ
// messaging (registering this module's consumers), and OpenTelemetry traces + metrics over OTLP.
builder.Services.AddInfrastructure(
    DiagnosticsConfig.ServiceName,
    [PaymentsModule.ConfigureConsumers()],
    rabbitMqSettings,
    databaseConnectionString,
    redisConnectionString,
    // An unreachable Redis degrades to an in-process cache and an in-process lock in local
    // development only — anywhere else the host keeps the reconnecting Redis connection and lets
    // the health check below report it unhealthy. See docs/caching.md.
    allowInMemoryCacheFallback: builder.Environment.IsDevelopment());

// No AddModuleDiagnostics(PaymentsDiagnostics.Name) yet: this service declares no instrument of its
// own until the observability milestone, and an ActivitySource/Meter name registered here before
// anything records into it would only be noise. The call goes in alongside PaymentsDiagnostics —
// an unregistered source or meter never errors, it silently records into nothing.

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
// endpoints, outbox/inbox job schedules (see PaymentsModule).
builder.Services.AddPaymentsModule(builder.Configuration);

WebApplication app = builder.Build();

// Feature 3.7 Milestone E — run the AddRequiredConfiguration and options checks HERE rather than
// leaving them to ValidateOnStart(), which defers them into app.RunAsync(): that is after the
// migration below, so a host missing its Stripe key would otherwise touch the database first.
app.Services.GetRequiredService<IStartupValidator>().Validate();

// EF Core migrations are applied automatically at startup — no manual `dotnet ef database update`.
app.ApplyMigrations();

// GET /health/live (the process only), GET /health/ready (its dependencies) and the unchanged
// aggregate GET /health — one shared mapping, so every host exposes an identical probe contract.
app.MapHealthProbes();

// One shared middleware for every host (Common.Presentation/Security): nosniff, DENY framing,
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

// The OpenAPI document and the two UIs over it, at /docs/payments/{openapi,scalar,swagger}
// — Feature 3.7 Milestone G. Mapped in EVERY environment now rather than only in Development:
// a documented surface that is invisible from the environments other people use documents
// nothing. Outside Development it requires a token. It has to come after UseAuthentication(),
// because that gate reads HttpContext.User.
app.MapApiDocumentation(allowAnonymous: app.Environment.IsDevelopment());

await app.RunAsync();

public partial class Program;
