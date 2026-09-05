using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FoodDeliveryService.Common.Presentation.Security;

/// <summary>
/// Declares the configuration keys a host cannot start without — Feature 3.7 Milestone E.
/// </summary>
public static class RequiredConfigurationExtensions
{
    /// <summary>
    /// Registers <paramref name="keys"/> as required outside Development and validates them during
    /// host startup. Call it as many times as makes sense — the keys accumulate into one options
    /// instance, so a boot missing three of them reports all three rather than the first.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configuration">The configuration the host was built with.</param>
    /// <param name="environment">The host environment; validation is skipped in Development.</param>
    /// <param name="keys">Colon-separated configuration keys, e.g. <c>Clients:Confidential:ClientSecret</c>.</param>
    public static IServiceCollection AddRequiredConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(keys);

        services.TryAddEnumerable(ServiceDescriptor
            .Singleton<IValidateOptions<RequiredConfigurationOptions>, RequiredConfigurationValidator>());

        services
            .AddOptions<RequiredConfigurationOptions>()
            .Configure(options =>
            {
                options.Enforced = !environment.IsDevelopment();
                options.EnvironmentName = environment.EnvironmentName;

                foreach (string key in keys)
                {
                    options.Values[key] = configuration[key];
                }
            })
            .ValidateOnStart();

        return services;
    }
}
