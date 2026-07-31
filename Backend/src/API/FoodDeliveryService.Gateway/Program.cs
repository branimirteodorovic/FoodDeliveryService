using OpenTelemetry.Resources;
using Serilog;
using OpenTelemetry.Trace;
using FoodDeliveryService.Common.Presentation.Health;
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

// OpenTelemetry tracing → OTLP exporter → Jaeger (:4317, UI :16686). The extra
// "Yarp.ReverseProxy" activity source makes the proxy hop itself visible, so a Jaeger trace
// shows Gateway → service → database/bus end to end.
builder.Services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(DiagnosticsConfig.ServiceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource("Yarp.ReverseProxy");

                tracing.AddOtlpExporter();
            });

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
