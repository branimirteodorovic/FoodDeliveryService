namespace FoodDeliveryService.Identity.OpenTelemetry;

/// <summary>
/// This host's identity for observability — the OpenTelemetry service name shown in Jaeger's
/// service dropdown and carried as <c>service.name</c> on every metric. Identity is not a module
/// host, so unlike the seven others this name has no MassTransit InstanceId role; it still follows
/// the same <c>OpenTelemetry/DiagnosticsConfig.cs</c> convention so the eight hosts read alike.
/// </summary>
internal static class DiagnosticsConfig
{
    public const string ServiceName = "FoodDeliveryService.Identity";
}
