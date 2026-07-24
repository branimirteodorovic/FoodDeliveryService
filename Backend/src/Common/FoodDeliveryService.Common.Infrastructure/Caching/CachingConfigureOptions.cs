using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace FoodDeliveryService.Common.Infrastructure.Caching;

/// <summary>
/// Binds <see cref="CachingSettings"/> from the "Caching" appsettings section. The infrastructure
/// bootstrap has no <see cref="IConfiguration"/> parameter of its own, so — like
/// <c>JwtBearerConfigureOptions</c> — this pulls it from DI instead.
/// </summary>
internal sealed class CachingConfigureOptions(IConfiguration configuration) : IConfigureOptions<CachingSettings>
{
    public void Configure(CachingSettings options)
    {
        configuration.GetSection(CachingSettings.SectionName).Bind(options);
    }
}
