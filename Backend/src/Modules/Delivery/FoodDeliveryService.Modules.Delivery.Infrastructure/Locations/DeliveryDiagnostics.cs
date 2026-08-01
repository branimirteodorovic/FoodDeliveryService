using System.Diagnostics;
using System.Diagnostics.Metrics;
using FoodDeliveryService.Common.Application.Diagnostics;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Locations;

/// <summary>
/// The Delivery module's custom telemetry surface, on the shared <see cref="AppDiagnostics"/>
/// convention. It carries one span — the "find nearest available driver" geo query, worth an
/// explicit span because a slow or empty candidate search is the first thing to look at when a
/// delivery won't assign — and the meter the assignment instruments are created on
/// (<c>DeliveryAssignmentDiagnostics</c>, in the Assignment folder next to the routine that records
/// them).
/// <para>
/// Both halves are wired by the single <c>AddModuleDiagnostics(Name)</c> call in the Delivery host's
/// Program.cs.
/// </para>
/// </summary>
public static class DeliveryDiagnostics
{
    public const string Name = "FoodDeliveryService.Delivery";

    private static readonly AppDiagnostics Diagnostics = new(Name);

    public static ActivitySource ActivitySource => Diagnostics.ActivitySource;

    public static Meter Meter => Diagnostics.Meter;
}
