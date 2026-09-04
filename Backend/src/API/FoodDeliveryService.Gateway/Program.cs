using Serilog;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using FoodDeliveryService.Common.Presentation.Correlation;
using FoodDeliveryService.Common.Presentation.Health;
using FoodDeliveryService.Common.Presentation.Security;
using FoodDeliveryService.Common.Presentation.Telemetry;
using FoodDeliveryService.Common.Presentation.RateLimiting;
using FoodDeliveryService.Gateway.OpenTelemetry;
using FoodDeliveryService.Gateway.Authentication;
using FoodDeliveryService.Gateway.RateLimiting;

// API Gateway — the single public entry point (:3000). Built on YARP (Yet Another Reverse
// Proxy), Microsoft's reverse-proxy library: it authenticates incoming requests and forwards
// them to the internal microservices, which are never exposed to clients directly.

var builder = WebApplication.CreateBuilder(args);

// Serilog structured logging, configured from the "Serilog" section in appsettings
// (sinks: Console + Seq at :5341, UI :8081 — same setup as every other service).
builder.Host.UseSerilog((context, loggerConfig) => loggerConfig.ReadFrom.Configuration(context.Configuration));

// Security response headers on every response, and no `Server: Kestrel` on any of them — Feature
// 3.7 Milestone D. The Add half exists separately from app.UseSecurityHeaders() below for one
// reason: KestrelServerOptions.AddServerHeader is read when the server starts and cannot be set from
// the pipeline.
builder.Services.AddSecurityHeaders(builder.Configuration);

// Feature 3.7 Milestone D §5.2 — and the one piece of this milestone that fixes a live defect rather
// than adding a precaution. Nothing in this repository terminates TLS, so the intended deployment
// puts a proxy in front of the Gateway; without this, RemoteIpAddress is that proxy's, and the edge
// rate limiter's per-client partition for anonymous callers collapses into a single global bucket
// (docs/rate-limiting.md §2). Nothing is trusted until a deployment names a proxy or a network — an
// unrestricted X-Forwarded-For would let a client choose its own partition key, which is worse than
// the bug. Gateway only: the module hosts are unreachable from a client.
builder.Services.AddEdgeForwardedHeaders(builder.Configuration);

// The browser policy, at the edge and nowhere else — the Angular SPA (Frontend/FRONTEND_PLAN.md)
// names it as its one backend prerequisite. Origins come from configuration and are empty by
// default, so the base appsettings.json is closed and each environment opens exactly what it needs.
builder.Services.AddEdgeCors(builder.Configuration);

// YARP: routes and clusters are pure configuration (appsettings "ReverseProxy" section).
// Requests are matched by path prefix (orders/**, users/**, restaurants/**, notifications/**)
// and proxied to the matching service's cluster. New endpoints under an existing prefix need
// no gateway change; only a brand-new prefix needs a new route + cluster there.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// OpenTelemetry traces + metrics → OTLP exporter (:4317; traces browsable in Jaeger at :16686).
// The same shared baseline the module hosts get through AddInfrastructure — the gateway used to
// hand-roll its own copy of it, which is how the two drifted apart in the first place.
builder.Services.AddHostTelemetry(DiagnosticsConfig.ServiceName);

// The proxy's own telemetry, which no other host has. The activity source makes the proxy hop
// visible, so a Jaeger trace shows Gateway → service → database/bus end to end; the meter of the
// same name carries the forwarding metrics (requests in flight, failures by reason) that explain a
// spike the downstream services don't account for.
const string YarpTelemetryName = "Yarp.ReverseProxy";

// ASP.NET Core's built-in rate-limiting meter. Nothing emits it until `UseEdgeRateLimiting` puts the
// middleware in the pipeline, which is the point — the limiter and its metrics arrive together.
const string RateLimitingTelemetryName = "Microsoft.AspNetCore.RateLimiting";

builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource(YarpTelemetryName));

builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddMeter(YarpTelemetryName));

// The rate limiter's own RED, from the framework's meter rather than a hand-rolled counter:
// `aspnetcore.rate_limiting.requests` carries the accepted/rejected split, plus lease duration and
// queue depth. It is the series that makes the rejected fraction of a load test explicit instead of
// showing up as client timeouts — which is the whole argument for shedding.
builder.Services.ConfigureOpenTelemetryMeterProvider(
    metrics => metrics.AddMeter(RateLimitingTelemetryName));

builder.Services.AddAuthorization();

// JWT Bearer validation against Duende IdentityServer (:18080). The gateway rejects
// unauthenticated traffic up front — each YARP route uses the "default" authorization policy,
// with users/register as the only "anonymous" route — and services still re-validate the token.
builder.Services.AddAuthentication().AddJwtBearer();

// Binds JwtBearerOptions from the "Authentication" appsettings section (Duende authority,
// audience, metadata address).
builder.Services.ConfigureOptions<JwtBearerConfigureOptions>();

// The gateway's first health check. Its readiness deliberately equals its liveness: the obvious
// readiness candidate — "are the downstream clusters up?" — is exactly what YARP exists to degrade
// around, and one dead cluster must not take the single public entry point, and with it every other
// service, out of rotation. Downstream health is each service's own /health/ready to report.
builder.Services.AddHealthChecks()
    .AddLivenessCheck(HealthCheckTags.Ready);

// Capacity guardrails — Feature 3.5 Milestone G. A global concurrency limit so throughput plateaus
// instead of collapsing past the knee, plus a per-client fixed window partitioned by subject (or IP
// when anonymous), sized per route tier so browsing is shed before a delivery being completed is.
// Counters live in the shared Redis, because per-pod buckets would multiply the limit by the replica
// count — the trap KUBERNETES_PHASE2_PLAN.md §5.4 names. See `docs/rate-limiting.md`.
//
// Redis is deliberately NOT added to the readiness check: the store fails open when it is
// unreachable, so a Redis outage degrades the guardrail rather than the gateway, and pulling the
// single public entry point out of rotation over it would be the same mistake as making downstream
// clusters a readiness dependency.
builder.Services.AddGatewayRateLimiting(
    builder.Configuration,
    allowInMemoryFallback: builder.Environment.IsDevelopment());

var app = builder.Build();

// First in the pipeline, ahead of correlation, request logging and the limiter: every one of them
// reads the client address or the scheme this rewrites, so a later placement would leave each of
// them reading the proxy's.
app.UseEdgeForwardedHeaders();

// GET /health/live + /health/ready + /health, the same contract every service exposes. Mapped
// before MapReverseProxy and matched by no YARP route, so they are served here and never proxied.
app.MapHealthProbes();

// One shared middleware for all nine hosts (Common.Presentation/Security): nosniff, DENY framing,
// no referrer, a `default-src 'none'` CSP for the JSON surface, and HSTS only when the request
// actually arrived over HTTPS. It is placed first so that a response short-circuited downstream — an
// authentication challenge, a rate-limit rejection, the exception handler — is stamped too.
app.UseSecurityHeaders();

// Where the platform's correlation id is born. This middleware reads an inbound X-Correlation-Id,
// or defaults it to the request's W3C trace id, then writes it back onto the REQUEST headers — YARP
// copies request headers to the proxied call, so the downstream service sees the same id and
// preserves it rather than minting its own, and no YARP transform is needed to carry it. The id is
// also echoed on the response and pushed into the Serilog LogContext (with the trace id, span id and
// service name), so one string off a failed response finds the Seq logs of every service that
// touched the request and the Jaeger trace they belong to.
app.UseRequestCorrelation();

app.UseSerilogRequestLogging();

// Before UseAuthentication, because a CORS preflight is an unauthenticated OPTIONS with no bearer
// token: applied after authentication it would be answered with a 401 and the browser would never
// send the real request. Here it short-circuits the preflight before the limiter and YARP see it.
// The policy applies to every proxied route — no YARP route sets its own CorsPolicy — which is what
// gives hubs/** the AllowCredentials the SignalR handshake needs.
app.UseEdgeCors();

app.UseAuthentication();

// After authentication, and that ordering is the design rather than a convenience: the per-client
// partition is the token's subject when there is one, and before this point HttpContext.User is
// empty, so every request would be counted against its IP — which is a whole office, a carrier NAT
// or a VPN, not a client. The cost is that a flood pays for JWT validation before being shed:
// signature verification against cached signing keys, no I/O, and far cheaper than the proxied round
// trip it prevents. Before UseAuthorization, so a 429 costs no policy evaluation either.
app.UseEdgeRateLimiting();

app.UseAuthorization();

// Hands all matched routes to the YARP proxy pipeline.
app.MapReverseProxy();

await app.RunAsync();
