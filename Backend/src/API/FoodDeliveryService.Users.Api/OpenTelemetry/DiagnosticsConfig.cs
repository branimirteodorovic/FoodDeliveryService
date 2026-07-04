namespace FoodDeliveryService.Users.Api.OpenTelemetry;

/// <summary>
/// This host's identity for observability and messaging: it becomes the OpenTelemetry service
/// name shown in Jaeger's service dropdown AND (kebab-cased) the MassTransit endpoint InstanceId
/// that suffixes this service's RabbitMQ queue names. It must therefore be unique per service —
/// a duplicate would make two services consume from the same queues.
/// </summary>
internal static class DiagnosticsConfig
{
    public const string ServiceName = "FoodDeliveryService.Users.Api";
}
