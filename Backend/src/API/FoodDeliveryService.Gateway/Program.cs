using Serilog;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using FoodDeliveryService.Common.Presentation.Health;
using FoodDeliveryService.Common.Presentation.Telemetry;
using FoodDeliveryService.Gateway.OpenTelemetry;
using FoodDeliveryService.Gateway.Authentication;
using FoodDeliveryService.Gateway.Middleware;

// API Gateway — the single public entry point (:3000). Built on YARP (Yet Another Reverse
// Proxy), Microsoft's reverse-proxy library: it authenticates incoming requests and forwards
// them to the internal microservices, which are never exposed to clients directly.

var builder = WebApplication.CreateBuilder(args);

// Serilog structured logging, configured from the "Serilog" section in appsettings
// (sinks: Console + Seq at :5341, UI :8081 — same setup as every other service).
builder.Host.UseSerilog((context, loggerConfig) => loggerConfig.ReadFrom.Configuration(context.Configuration));

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

builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource(YarpTelemetryName));

builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddMeter(YarpTelemetryName));

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

var app = builder.Build();

// GET /health/live + /health/ready + /health, the same contract every service exposes. Mapped
// before MapReverseProxy and matched by no YARP route, so they are served here and never proxied.
app.MapHealthProbes();

// Pushes trace/correlation ids into the Serilog LogContext so gateway logs in Seq can be
// correlated with the Jaeger trace of the same request.
app.UseLogContext();

app.UseSerilogRequestLogging();

app.UseAuthentication();

app.UseAuthorization();

// Hands all matched routes to the YARP proxy pipeline.
app.MapReverseProxy();

await app.RunAsync();
