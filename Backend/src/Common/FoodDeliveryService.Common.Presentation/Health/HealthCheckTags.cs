namespace FoodDeliveryService.Common.Presentation.Health;

/// <summary>
/// The only two health-check tags in the system. Every registered check must carry at least one of
/// them: an untagged check is reported by the aggregate <c>GET /health</c> but is invisible to both
/// probes, which is a silent hole rather than a loud failure.
/// See <c>docs/health-probe-contract.md</c>.
/// </summary>
public static class HealthCheckTags
{
    /// <summary>
    /// "The process is up and answering." Carried by the dependency-free <c>self</c> check that
    /// <see cref="HealthChecksBuilderExtensions.AddLivenessCheck"/> registers, and by nothing an
    /// outage elsewhere can break — a liveness failure restarts the container, and restarting a pod
    /// does not bring PostgreSQL back, it just adds a crash loop to the incident.
    /// </summary>
    public const string Live = "live";

    /// <summary>
    /// "Every external dependency is reachable." Carried by every dependency check. A readiness
    /// failure pulls the pod out of the load-balancer rotation but leaves it running, so it rejoins
    /// by itself once the dependency recovers.
    /// </summary>
    public const string Ready = "ready";
}
