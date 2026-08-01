using FoodDeliveryService.Common.Application.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace FoodDeliveryService.Common.Infrastructure.Diagnostics;

public static class DiagnosticsRegistrationExtensions
{
    /// <summary>
    /// Registers a module's <see cref="AppDiagnostics"/> names with BOTH OpenTelemetry providers —
    /// <c>AddSource</c> on the tracer, <c>AddMeter</c> on the meter — so a module declares its
    /// telemetry surface once and its host wires it in one line.
    /// <para>
    /// An unregistered source or meter is not an error at any layer: the instrument exists, the code
    /// records into it, and the measurements go nowhere. Registering both halves together is what
    /// keeps a module from shipping a counter that no backend ever sees.
    /// </para>
    /// </summary>
    /// <param name="names">
    /// The <see cref="AppDiagnostics.Name"/> of each module diagnostics class the host owns. Also
    /// takes a bare meter name (<c>CacheDiagnostics.MeterName</c>) — registering an activity source
    /// that never starts an activity costs nothing.
    /// </param>
    public static IServiceCollection AddModuleDiagnostics(this IServiceCollection services, params string[] names)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(names);

        services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource(names));
        services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddMeter(names));

        return services;
    }
}
