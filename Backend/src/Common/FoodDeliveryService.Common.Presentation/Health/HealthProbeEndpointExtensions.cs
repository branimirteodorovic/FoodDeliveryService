using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FoodDeliveryService.Common.Presentation.Health;

/// <summary>
/// The single place the probe endpoints are shaped, so all eight hosts expose an identical contract
/// (see <c>docs/health-probe-contract.md</c>, consumed by Feature 2.5's pod probes and by every host
/// added after it).
/// </summary>
public static class HealthProbeEndpointExtensions
{
    public const string HealthPath = "health";
    public const string LivenessPath = "health/live";
    public const string ReadinessPath = "health/ready";

    /// <summary>
    /// A probe is a binary signal to a kubelet, so anything short of Healthy must read as "do not
    /// send me traffic". The framework default maps Degraded to 200, which would leave a degraded
    /// pod in rotation; the two probes override it. The aggregate <c>/health</c> deliberately keeps
    /// the framework defaults so it stays byte-for-byte what it was before this split.
    /// </summary>
    private static readonly IDictionary<HealthStatus, int> ProbeStatusCodes =
        new Dictionary<HealthStatus, int>
        {
            [HealthStatus.Healthy] = StatusCodes.Status200OK,
            [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
            [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
        };

    /// <summary>
    /// Maps the three health endpoints every host exposes:
    /// <list type="bullet">
    /// <item><c>GET /health/live</c> — checks tagged <see cref="HealthCheckTags.Live"/>; bind to the
    /// <c>livenessProbe</c>. Never fails on a dependency.</item>
    /// <item><c>GET /health/ready</c> — checks tagged <see cref="HealthCheckTags.Ready"/>; bind to
    /// the <c>readinessProbe</c>.</item>
    /// <item><c>GET /health</c> — all checks, unfiltered; the pre-existing aggregate for humans and
    /// dashboards, unchanged.</item>
    /// </list>
    /// All three render the HealthChecks.UI JSON payload, but a probe must key on the status code,
    /// not the body. The endpoints are explicitly anonymous so the kubelet — which carries no token —
    /// reaches them regardless of the host's authorization defaults.
    /// </summary>
    public static IEndpointRouteBuilder MapHealthProbes(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks(LivenessPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthCheckTags.Live),
            ResultStatusCodes = ProbeStatusCodes,
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        }).AllowAnonymous();

        app.MapHealthChecks(ReadinessPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthCheckTags.Ready),
            ResultStatusCodes = ProbeStatusCodes,
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        }).AllowAnonymous();

        // The aggregate that existed before the split: every check, framework status mapping,
        // same HealthChecks.UI response writer. Nothing consumes it automatically — it is the
        // human-facing view that says which dependency is the one that is down.
        app.MapHealthChecks(HealthPath, new HealthCheckOptions
        {
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        }).AllowAnonymous();

        return app;
    }
}
