using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace FoodDeliveryService.Common.Infrastructure.Authentication;

/// <summary>
/// Binds <see cref="JwtBearerOptions"/> straight from the "Authentication" appsettings section
/// (audience, valid issuers, metadata address). The metadata address points at Duende
/// IdentityServer's OpenID Connect discovery endpoint, so each service fetches the signing keys
/// from there and validates JWTs locally — no per-request call to the identity provider.
/// </summary>
internal sealed class JwtBearerConfigureOptions(IConfiguration configuration)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    private const string ConfigurationSectionName = "Authentication";

    public void Configure(JwtBearerOptions options)
    {
        configuration.GetSection(ConfigurationSectionName).Bind(options);
    }

    public void Configure(string? name, JwtBearerOptions options)
    {
        Configure(options);
    }
}
