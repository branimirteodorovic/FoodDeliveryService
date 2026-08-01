using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FoodDeliveryService.Common.Application.Diagnostics;

/// <summary>
/// A module's custom telemetry surface: one name, carrying both an <see cref="ActivitySource"/> for
/// spans the generic instrumentation doesn't cover and a <see cref="Meter"/> for its own
/// instruments. Declared once as a static field on a module's <c>{Module}Diagnostics</c> class and
/// registered with <c>AddModuleDiagnostics</c> (<c>Common.Infrastructure.Diagnostics</c>) in that
/// module's host.
/// <para>
/// The pair is the point. Delivery and Real-Time each grew an identical hand-rolled
/// <c>ActivitySource</c> holder with a matching hand-written <c>AddSource</c> line in its host —
/// which meant every module that later wanted a counter would have invented a second, separately
/// registered convention for it, and the first forgotten <c>AddMeter</c> would have been a silently
/// dead instrument rather than a build error.
/// </para>
/// <para>
/// It lives in the <b>Application</b> layer, not Infrastructure where Milestone A first put it,
/// because the code that owns a business measurement is a handler: <c>OrdersDiagnostics</c> is
/// recorded from the module's domain-event handlers and <c>ApplicationDiagnostics</c> from a
/// pipeline behavior, and neither assembly may reference <c>Common.Infrastructure</c>. Nothing here
/// depends on OpenTelemetry — both types are BCL — so only the <c>AddModuleDiagnostics</c>
/// registration extension, which does touch the OTel providers, stays behind in Infrastructure.
/// </para>
/// </summary>
public sealed class AppDiagnostics
{
    /// <param name="name">
    /// The shared source/meter name — <c>FoodDeliveryService.{Module}</c>. It is what the host
    /// passes to <c>AddModuleDiagnostics</c>, so both providers listen under one string.
    /// </param>
    public AppDiagnostics(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        ActivitySource = new ActivitySource(name);
        Meter = new Meter(name);
    }

    public string Name { get; }

    /// <summary>
    /// Spans for the operations the built-in instrumentation can't see — a Redis GEO candidate
    /// search, a pub/sub fan-out. Produces a live <see cref="Activity"/> only while a listener is
    /// attached, which is what registration buys.
    /// </summary>
    public ActivitySource ActivitySource { get; }

    /// <summary>
    /// The module's own instruments. Created directly rather than through <c>IMeterFactory</c>
    /// because the consumers are static holders reached from anywhere in the module (the same shape
    /// <c>CacheDiagnostics</c> uses); the trade-off is that the meter is process-wide, so tests
    /// filter measurements by tag rather than by scope.
    /// </summary>
    public Meter Meter { get; }
}
