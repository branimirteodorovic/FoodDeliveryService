using System.Diagnostics.Metrics;
using FoodDeliveryService.Common.Infrastructure.Diagnostics;

namespace FoodDeliveryService.Modules.Orders.IntegrationTests.Telemetry;

/// <summary>
/// A module diagnostics class shaped exactly like <c>DeliveryDiagnostics</c> and
/// <c>RealTimeDiagnostics</c>, declared here so <see cref="MetricsTests"/> can prove the path a real
/// module counter takes end to end: declared on <see cref="AppDiagnostics"/>, registered by the one
/// <c>AddModuleDiagnostics</c> call a host makes, collected by the meter provider
/// <c>AddInfrastructure</c> stands up.
/// </summary>
internal static class SmokeDiagnostics
{
    public const string Name = "FoodDeliveryService.Orders.IntegrationTests";

    private static readonly AppDiagnostics Diagnostics = new(Name);

    public static readonly Counter<long> Operations = Diagnostics.Meter.CreateCounter<long>(
        "smoke.operations",
        unit: "{operation}",
        description: "Test counter recorded by the telemetry integration tests.");
}
