using System.Diagnostics;

namespace FoodDeliveryService.Modules.RealTime.Infrastructure.RealTime;

/// <summary>
/// Named tracing source for RealTime-specific spans that the generic instrumentation doesn't cover.
/// The Redis pub/sub location forward (Milestone C) is not auto-instrumented like the MassTransit
/// consumers are, and a stuck moving pin is exactly the kind of thing that needs to be debuggable.
/// Registered with OpenTelemetry via AddSource in the RealTime host's Program.cs.
/// </summary>
public static class RealTimeDiagnostics
{
    public const string SourceName = "FoodDeliveryService.RealTime";

    public static readonly ActivitySource ActivitySource = new(SourceName);
}
