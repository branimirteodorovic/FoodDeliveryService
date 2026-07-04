namespace FoodDeliveryService.Gateway.OpenTelemetry;

/// <summary>
/// The OpenTelemetry service name for the gateway — this is the label under which YARP proxy
/// spans appear in Jaeger's service dropdown.
/// </summary>
internal static class DiagnosticsConfig
{
    public const string ServiceName = "FoodDeliveryService.Gateway";
}
