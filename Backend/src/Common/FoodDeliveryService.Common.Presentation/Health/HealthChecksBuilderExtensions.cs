using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FoodDeliveryService.Common.Presentation.Health;

public static class HealthChecksBuilderExtensions
{
    /// <summary>
    /// The one check behind <c>GET /health/live</c>. Its name in the probe payload.
    /// </summary>
    public const string LivenessCheckName = "self";

    /// <summary>
    /// Registers the dependency-free <c>self</c> check tagged <see cref="HealthCheckTags.Live"/>.
    /// It returns <see cref="HealthStatus.Healthy"/> unconditionally and touches nothing external:
    /// reaching it at all is the signal — the host started, the pipeline is built and the process is
    /// answering HTTP.
    /// </summary>
    /// <param name="additionalTags">
    /// Extra tags for the self check. Used only by the Gateway, which passes
    /// <see cref="HealthCheckTags.Ready"/> so its readiness equals its liveness: the obvious
    /// readiness candidate — "are the downstream clusters up?" — is exactly what YARP exists to
    /// degrade around, and one dead cluster must not take the single public entry point (and with it
    /// every other service) out of rotation.
    /// </param>
    public static IHealthChecksBuilder AddLivenessCheck(
        this IHealthChecksBuilder builder,
        params string[] additionalTags)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(additionalTags);

        string[] tags = [HealthCheckTags.Live, .. additionalTags];

        return builder.AddCheck(
            LivenessCheckName,
            () => HealthCheckResult.Healthy("The process is up and answering."),
            tags);
    }
}
