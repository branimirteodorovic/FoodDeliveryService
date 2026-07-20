using FoodDeliveryService.Common.Infrastructure.Configuration;

namespace FoodDeliveryService.RealTime.Api.Extensions;

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
