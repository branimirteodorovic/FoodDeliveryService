using FoodDeliveryService.Common.Infrastructure.Configuration;
using FoodDeliveryService.Common.Presentation.Health;

namespace FoodDeliveryService.FraudDetection.Api.Extensions;

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
