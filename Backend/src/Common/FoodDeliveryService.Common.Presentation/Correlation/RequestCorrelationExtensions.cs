using FoodDeliveryService.Common.Presentation.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FoodDeliveryService.Common.Presentation.Correlation;

/// <summary>
/// The one line every host adds to its pipeline for correlation — the counterpart of
/// <c>MapHealthProbes()</c> and <c>AddHostTelemetry()</c>, and for the same reason: eight hosts
/// editing eight identical copies is how they drifted in the first place.
/// </summary>
public static class RequestCorrelationExtensions
{
    /// <summary>
    /// Stamps/echoes <c>X-Correlation-Id</c> and pushes the trace, span, service and business
    /// context into the Serilog <c>LogContext</c> for the rest of the request.
    /// <para>
    /// Place it early — before <c>UseSerilogRequestLogging()</c>, the exception handler and the
    /// endpoints — so everything downstream logs inside the scope. It sits after the routing
    /// middleware <c>WebApplication</c> inserts at the top of the pipeline, which is what makes the
    /// matched route's ids available to it.
    /// </para>
    /// </summary>
    public static IApplicationBuilder UseRequestCorrelation(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Registered by AddHostTelemetry, which every host calls — directly (Gateway, Identity) or
        // through AddInfrastructure (the six module hosts). The fallback keeps a host that only
        // wants correlation from having to take the telemetry pillar too.
        HostServiceName serviceName =
            app.ApplicationServices.GetService<HostServiceName>() ??
            new HostServiceName(app.ApplicationServices.GetRequiredService<IHostEnvironment>().ApplicationName);

        // Order matters: the id is resolved first so the log scope can carry it.
        app.UseMiddleware<CorrelationIdMiddleware>();

        app.UseMiddleware<LogContextTraceLoggingMiddleware>(serviceName);

        return app;
    }
}
