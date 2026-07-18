using System.Diagnostics;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Locations;

/// <summary>
/// Named tracing source for Delivery-specific spans that the generic instrumentation doesn't
/// cover. Right now that is only the "find nearest available driver" geo query — worth an explicit
/// span because a slow or empty candidate search is the first thing to look at when a delivery
/// won't assign. Registered with OpenTelemetry via AddSource in the Delivery host's Program.cs.
/// </summary>
public static class DeliveryDiagnostics
{
    public const string SourceName = "FoodDeliveryService.Delivery";

    public static readonly ActivitySource ActivitySource = new(SourceName);
}
