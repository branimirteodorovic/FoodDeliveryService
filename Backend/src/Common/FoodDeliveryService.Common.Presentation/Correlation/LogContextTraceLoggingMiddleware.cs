using System.Diagnostics;
using FoodDeliveryService.Common.Presentation.Telemetry;
using Microsoft.AspNetCore.Http;
using Serilog.Context;
using Serilog.Core;
using Serilog.Core.Enrichers;

namespace FoodDeliveryService.Common.Presentation.Correlation;

/// <summary>
/// Puts the request's telemetry context on every log line it produces: <c>TraceId</c> and
/// <c>SpanId</c> from the ambient <see cref="Activity"/>, the <c>ServiceName</c> the same host
/// reports to Jaeger, the <c>CorrelationId</c> the client can see, and any business id on the route.
/// A Seq line is then one click from its Jaeger trace, and one query from every other line about the
/// same order.
/// <para>
/// This was seven near-identical copies, one per host under <c>src/API/**/Middleware/</c>, plus the
/// Identity host that had none at all — so the service whose token endpoint every authenticated
/// request passes through logged nothing correlatable. One implementation here, used by all eight.
/// </para>
/// </summary>
internal sealed class LogContextTraceLoggingMiddleware(RequestDelegate next, HostServiceName serviceName)
{
    public async Task Invoke(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Note the await INSIDE the scope. The copies this replaces did `return next.Invoke(context)`
        // from inside a `using`, which disposed the scope as soon as the first await yielded — the
        // properties survived only the synchronous head of the pipeline, which is not where the
        // interesting logs are.
        using (LogContext.Push(BuildEnrichers(context)))
        {
            await next(context);
        }
    }

    private ILogEventEnricher[] BuildEnrichers(HttpContext context)
    {
        // ServiceName is the OpenTelemetry resource attribute, not Serilog's own "Application"
        // property (which comes from appsettings and names the process): a log filter and a trace
        // filter take the same value.
        List<ILogEventEnricher> enrichers = [new PropertyEnricher("ServiceName", serviceName.Value)];

        // Null when nothing listens to the ASP.NET Core activity source — a host with tracing
        // switched off still logs, it just has no trace to point at, so the properties are omitted
        // rather than written as null.
        Activity? activity = Activity.Current;

        if (activity is not null)
        {
            enrichers.Add(new PropertyEnricher("TraceId", activity.TraceId.ToString()));

            // The half that was missing: TraceId alone finds the trace, SpanId finds the operation
            // inside it that wrote the line.
            enrichers.Add(new PropertyEnricher("SpanId", activity.SpanId.ToString()));
        }

        if (context.Items.TryGetValue(CorrelationHeaders.CorrelationIdItemKey, out object? correlationId) &&
            correlationId is string id)
        {
            enrichers.Add(new PropertyEnricher("CorrelationId", id));
        }

        foreach (KeyValuePair<string, string> businessId in BusinessIdRouteValues.Extract(context))
        {
            enrichers.Add(new PropertyEnricher(businessId.Key, businessId.Value));
        }

        return [.. enrichers];
    }
}
