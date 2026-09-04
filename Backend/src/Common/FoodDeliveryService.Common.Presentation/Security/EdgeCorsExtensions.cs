using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FoodDeliveryService.Common.Presentation.Security;

/// <summary>
/// The browser policy for the platform — Feature 3.7 Milestone D.
/// <para>
/// <b>Edge only</b>, for the same reason the rate limiter is (see
/// <c>EdgeRateLimitingExtensions</c>): CORS is a property of the boundary between the platform and a
/// browser, and the Gateway is the only thing a browser ever talks to (Hard Rule 10). A per-service
/// policy would be seven copies of one list, drifting, in front of services no browser can reach.
/// </para>
/// <para>
/// It lives in <c>Common.Presentation</c> rather than in the Gateway because the Gateway takes no
/// <c>Common.Infrastructure</c> dependency and this is where its cross-cutting pieces already live —
/// and because putting it here is what lets <c>Common.UnitTests</c> assert the built policy.
/// </para>
/// </summary>
public static class EdgeCorsExtensions
{
    private const string LoggerCategory = "FoodDeliveryService.Security";

    /// <summary>
    /// Registers the single named policy from the <c>"Cors"</c> section. Pair with
    /// <see cref="UseEdgeCors"/>.
    /// </summary>
    public static IServiceCollection AddEdgeCors(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new EdgeCorsOptions();

        IConfigurationSection section = configuration.GetSection(EdgeCorsOptions.SectionName);

        section.Bind(options);

        // Bind() appends rather than replaces, and ExposedHeaders ships two defaults. See
        // ConfiguredArray — this is why a deployment can genuinely narrow the list.
        options.ExposedHeaders = ConfiguredArray.Replace(
            section,
            nameof(EdgeCorsOptions.ExposedHeaders),
            options.ExposedHeaders);

        // Refused here rather than at the first preflight. `AllowAnyOrigin` + `AllowCredentials` is
        // the combination the CORS spec forbids — a browser rejects `Access-Control-Allow-Origin: *`
        // on a credentialed request — and ASP.NET Core throws when the policy is *built*, which for a
        // policy resolved per request means the failure surfaces as a 500 on someone's first login
        // rather than at boot. Naming the offending value at startup is the difference between a
        // five-second fix and an afternoon.
        if (options.AllowCredentials && Array.Exists(options.AllowedOrigins, origin => origin is "*"))
        {
            throw new InvalidOperationException(
                $"'{EdgeCorsOptions.SectionName}:{nameof(EdgeCorsOptions.AllowedOrigins)}' contains '*' while " +
                $"'{nameof(EdgeCorsOptions.AllowCredentials)}' is true. A wildcard origin cannot carry " +
                "credentials — list the SPA's exact origins, or set AllowCredentials to false.");
        }

        services.AddSingleton(options);

        services.AddCors(cors => cors.AddPolicy(EdgeCorsOptions.PolicyName, policy => Build(policy, options)));

        return services;
    }

    /// <summary>
    /// Adds the CORS middleware, applying the named policy to every endpoint that does not carry its
    /// own CORS metadata — which is every YARP route, because none of them sets <c>CorsPolicy</c>.
    /// <para>
    /// Placement: after <c>UseRequestCorrelation()</c> and <b>before</b> <c>UseAuthentication()</c>.
    /// A preflight is an unauthenticated <c>OPTIONS</c> with no <c>Authorization</c> header, so a
    /// policy applied after authentication answers it with a 401 and the browser never sends the real
    /// request. Sitting here, the middleware short-circuits the preflight before the limiter and the
    /// proxy ever see it.
    /// </para>
    /// </summary>
    public static IApplicationBuilder UseEdgeCors(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.ApplicationServices.GetService<EdgeCorsOptions>()
            ?? throw new InvalidOperationException(
                $"{nameof(UseEdgeCors)}() requires {nameof(AddEdgeCors)}(configuration) on the host's services.");

        ILogger logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);

        if (options.HasOrigins)
        {
            logger.LogInformation(
                "CORS is on for {OriginCount} origin(s): {Origins}. Credentials {Credentials}; " +
                "exposed headers: {ExposedHeaders}.",
                options.AllowedOrigins.Length,
                string.Join(", ", options.AllowedOrigins),
                options.AllowCredentials ? "allowed" : "not allowed",
                string.Join(", ", options.ExposedHeaders));
        }
        else
        {
            // Not a warning: this is the correct state for every environment that has no browser
            // client, and the base appsettings.json ships it deliberately. It is logged because
            // "the SPA gets a CORS error" is otherwise diagnosed from the browser console alone.
            logger.LogInformation(
                "CORS is configured with no allowed origins, so every cross-origin browser request " +
                "will be refused. Set '{Section}:{Key}' to the SPA's origin(s) to change that.",
                EdgeCorsOptions.SectionName,
                nameof(EdgeCorsOptions.AllowedOrigins));
        }

        return app.UseCors(EdgeCorsOptions.PolicyName);
    }

    /// <summary>Builds the named policy. Asserted in the tests by resolving it from <see cref="CorsOptions"/>.</summary>
    private static void Build(CorsPolicyBuilder policy, EdgeCorsOptions options)
    {
        policy
            .WithOrigins(options.AllowedOrigins)
            // Any header and any method: the Gateway proxies seven services whose surface changes
            // every milestone, and an allow-list of headers here would be a fourth place to remember
            // to edit. It grants a browser nothing it could not already do same-origin — the origin
            // list is the control that matters.
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders(options.ExposedHeaders)
            .SetPreflightMaxAge(TimeSpan.FromSeconds(Math.Max(options.PreflightMaxAgeSeconds, 0)));

        if (options.AllowCredentials)
        {
            policy.AllowCredentials();
        }
    }
}
