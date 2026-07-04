using FoodDeliveryService.Common.Infrastructure.Configuration;

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
        builder.AddUrlGroup(healthUri, HttpMethod.Get, DuendeHealthCheck);

        return builder;
    }

    internal static Uri GetDuendeHealthUrl(this IConfiguration configuration)
    {
        return new Uri(configuration.GetValueOrThrow<string>(DuendeHealthUrl));
    }
}
