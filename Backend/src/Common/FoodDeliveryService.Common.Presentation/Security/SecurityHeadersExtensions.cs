using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FoodDeliveryService.Common.Presentation.Security;

/// <summary>
/// The two lines every host adds for its security response headers — the counterpart of
/// <c>UseRequestCorrelation()</c> and <c>MapHealthProbes()</c>, and for the same reason: nine hosts
/// maintaining nine copies of a header list is how they drift, and a header that is present on eight
/// hosts is a header a reviewer cannot rely on.
/// </summary>
public static class SecurityHeadersExtensions
{
    private const string LoggerCategory = "FoodDeliveryService.Security";

    /// <summary>
    /// Binds <see cref="SecurityHeadersOptions"/> and suppresses Kestrel's <c>Server</c> header.
    /// <para>
    /// This is a separate call from <see cref="UseSecurityHeaders"/> for exactly one reason:
    /// <see cref="KestrelServerOptions.AddServerHeader"/> is read when the server starts, so it
    /// cannot be set from the pipeline. Everything else here could have lived in the <c>Use</c> call.
    /// </para>
    /// </summary>
    public static IServiceCollection AddSecurityHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new SecurityHeadersOptions();

        IConfigurationSection section = configuration.GetSection(SecurityHeadersOptions.SectionName);

        section.Bind(options);

        // Bind() appends to an array that already has values; the carve-out list has four defaults,
        // so a deployment narrowing it would otherwise keep them. See ConfiguredArray.
        options.DocumentationPathPrefixes = ConfiguredArray.Replace(
            section,
            nameof(SecurityHeadersOptions.DocumentationPathPrefixes),
            options.DocumentationPathPrefixes);

        services.AddSingleton(options);

        // Free reconnaissance otherwise: `Server: Kestrel` tells a scanner the stack and, with it,
        // which CVE list to work through. It is the one header this milestone removes rather than
        // adds.
        services.Configure<KestrelServerOptions>(kestrel => kestrel.AddServerHeader = false);

        return services;
    }

    /// <summary>
    /// Adds the header middleware to the pipeline. Place it <b>first</b> — before
    /// <c>UseRequestCorrelation()</c> — so that a response short-circuited by anything downstream
    /// (an authentication challenge, a rate-limit rejection, the exception handler) is still stamped.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.ApplicationServices.GetService<SecurityHeadersOptions>()
            ?? throw new InvalidOperationException(
                $"{nameof(UseSecurityHeaders)}() requires {nameof(AddSecurityHeaders)}(configuration) " +
                "on the host's services — it is what binds the options and turns off Kestrel's Server header.");

        if (!options.Enabled)
        {
            app.ApplicationServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(LoggerCategory)
                .LogWarning(
                    "Security response headers are DISABLED. Responses will carry no CSP, no " +
                    "X-Content-Type-Options and no X-Frame-Options.");

            return app;
        }

        return app.UseMiddleware<SecurityHeadersMiddleware>(options);
    }
}
