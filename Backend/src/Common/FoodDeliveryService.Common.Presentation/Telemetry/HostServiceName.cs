namespace FoodDeliveryService.Common.Presentation.Telemetry;

/// <summary>
/// The host's OpenTelemetry resource service name (<c>DiagnosticsConfig.ServiceName</c>), registered
/// by <see cref="HostTelemetryExtensions.AddHostTelemetry"/> so anything outside the telemetry
/// pipeline can name the service the same way the telemetry does.
/// <para>
/// Its one consumer today is the log-scope enrichment: a Seq log line and the Jaeger span it belongs
/// to must agree on <c>service.name</c>, and they only do so for free if both read the same value.
/// Taking it from DI rather than from a second constant is what stops the two from drifting when a
/// host is renamed.
/// </para>
/// </summary>
public sealed record HostServiceName(string Value);
