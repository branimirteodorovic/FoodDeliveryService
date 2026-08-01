using System.Diagnostics;
using System.Diagnostics.Metrics;
using FoodDeliveryService.Common.Application.Diagnostics;

namespace FoodDeliveryService.Modules.RealTime.Infrastructure.RealTime;

/// <summary>
/// The Real-Time module's custom telemetry surface, on the shared <see cref="AppDiagnostics"/>
/// convention. The Redis pub/sub location forward is not auto-instrumented the way the MassTransit
/// consumers are, and a stuck moving pin is exactly the kind of thing that needs to be debuggable;
/// the meter is registered and waiting for socket-level instruments (connections active, frames
/// sent) whenever the Real-Time plan wants them.
/// <para>
/// Both halves are wired by the single <c>AddModuleDiagnostics(Name)</c> call in the Real-Time
/// host's Program.cs.
/// </para>
/// </summary>
public static class RealTimeDiagnostics
{
    public const string Name = "FoodDeliveryService.RealTime";

    private static readonly AppDiagnostics Diagnostics = new(Name);

    public static ActivitySource ActivitySource => Diagnostics.ActivitySource;

    public static Meter Meter => Diagnostics.Meter;
}
