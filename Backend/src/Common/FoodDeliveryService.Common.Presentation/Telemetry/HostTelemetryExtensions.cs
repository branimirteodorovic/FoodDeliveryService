using FoodDeliveryService.Common.Presentation.Correlation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace FoodDeliveryService.Common.Presentation.Telemetry;

/// <summary>
/// The OpenTelemetry baseline every host gets, whatever else it is: a service-named resource, the
/// transport-level tracing and metrics instrumentation, host/runtime metrics, and the OTLP exporter
/// on both pillars (endpoint from the standard <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> setting).
/// <para>
/// It lives in <c>Common.Presentation</c> for the same reason the health probes do: the Gateway and
/// Identity are not module hosts and take no <c>Common.Infrastructure</c> dependency, yet they need
/// exactly this wiring. <c>AddInfrastructure</c> calls it too and layers the module-only sources and
/// meters (EF Core, Npgsql, Redis, MassTransit, cache) on top, so there is one definition of the
/// baseline rather than three drifting copies — which is what the Gateway's hand-rolled block had
/// already become.
/// </para>
/// </summary>
public static class HostTelemetryExtensions
{
    /// <summary>
    /// Wires the traces and metrics pillars for a host.
    /// </summary>
    /// <param name="serviceName">
    /// The resource service name — the Jaeger service dropdown entry and the <c>service.name</c>
    /// dimension on every metric. Comes from the host's <c>DiagnosticsConfig.ServiceName</c>.
    /// </param>
    /// <param name="exportLogsViaOtlp">
    /// Opt-in third pillar. Serilog → Seq stays the primary log path (it carries the trace
    /// correlation), so this is off by default: turning it on ships a second copy of every log
    /// record over OTLP, which is only worth it once a backend is there to receive it — Feature 2.4
    /// Milestone E, when the collector lands.
    /// </param>
    public static IServiceCollection AddHostTelemetry(
        this IServiceCollection services,
        string serviceName,
        bool exportLogsViaOtlp = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        // The same name the resource below reports, made resolvable so the log scope can stamp
        // service.name on every log line without a second constant to keep in sync
        // (UseRequestCorrelation).
        services.TryAddSingleton(new HostServiceName(serviceName));

        // The ambient correlation id + traceparent, populated by UseRequestCorrelation and read by
        // everything that has to correlate work with no HttpContext in sight — the outbox
        // interceptor, the MassTransit filters, the outbox/inbox dispatch jobs.
        services.TryAddSingleton<CorrelationContext>();

        IOpenTelemetryBuilder builder = services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName));

        // Tracing: the two instrumentations every host has a use for. A module host adds EF Core,
        // Npgsql, Redis and MassTransit through AddInfrastructure; the Gateway adds YARP itself.
        builder.WithTracing(tracing =>
        {
            tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation();

            tracing.AddOtlpExporter();
        });

        // Metrics: the RED baseline (http.server.request.duration and the HttpClient equivalent)
        // plus the host signals that explain it — GC pauses, thread-pool starvation and exception
        // counts from the runtime, CPU/memory from the process. All of it is emitted by the BCL and
        // ASP.NET Core already; until this reader existed nothing collected any of it.
        builder.WithMetrics(metrics =>
        {
            metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddProcessInstrumentation();

            // Same OTLP endpoint the traces use, so metrics need no configuration key of their own.
            metrics.AddOtlpExporter();
        });

        if (exportLogsViaOtlp)
        {
            builder.WithLogging(logging => logging.AddOtlpExporter());
        }

        return services;
    }
}
