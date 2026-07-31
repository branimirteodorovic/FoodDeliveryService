using FoodDeliveryService.Common.Infrastructure.Configuration;
using FoodDeliveryService.Common.Presentation.Health;

namespace FoodDeliveryService.Users.Api.Extensions;

/// <summary>
/// Adds a "Duende" entry to the service's health report by probing the IdentityServer host's
/// /health endpoint (URL from the "Duende:HealthUrl" setting) — if the identity provider is
/// down, no tokens can be issued, so this service is effectively degraded too.
/// </summary>
internal static class DuendeHealthChecksBuilderExtensions
{
    private const string DuendeHealthCheck = "Duende";
    private const string DuendeHealthUrl = "Duende:HealthUrl";

    internal static IHealthChecksBuilder AddDuende(this IHealthChecksBuilder builder, Uri healthUri)
    {
        // Tagged ready, not live: an unreachable Identity means this host cannot resolve permissions
        // and so cannot serve authenticated traffic — but restarting it would not help.
        builder.AddUrlGroup(healthUri, HttpMethod.Get, DuendeHealthCheck, tags: [HealthCheckTags.Ready]);

        return builder;
    }

    internal static Uri GetDuendeHealthUrl(this IConfiguration configuration)
    {
        return new Uri(configuration.GetValueOrThrow<string>(DuendeHealthUrl));
    }
}
