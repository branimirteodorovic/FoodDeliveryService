using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Scalar.AspNetCore;

namespace FoodDeliveryService.Common.Presentation.Documentation;

/// <summary>
/// The two lines every documented host adds for its API documentation — the counterpart of
/// <c>UseSecurityHeaders()</c>, <c>UseRequestCorrelation()</c> and <c>MapHealthProbes()</c>, and
/// hoisted here for the same reason: seven hosts were carrying seven byte-identical copies of a
/// <c>SwaggerExtensions.cs</c> whose one distinguishing feature was a title that described none of
/// them.
/// </summary>
/// <remarks>
/// <para>
/// What replaced those copies is smaller than what they contained. Swashbuckle's
/// <c>AddSwaggerGen</c> is gone: it built a <em>second</em> OpenAPI document, from a different
/// pipeline, that no host ever served — <c>UseSwagger()</c> was never called anywhere — while
/// <c>AddOpenApi()</c> built the one that was. Two generators is two documents to keep true, so
/// there is now one, produced by <c>Microsoft.AspNetCore.OpenApi</c> and rendered by both UIs.
/// Swashbuckle stays for its Swagger UI assets alone.
/// </para>
/// <para>
/// Everything is served under <c>/docs/{slug}/</c> so the Gateway can proxy it with the same
/// identity path transform every other route uses — see <see cref="ApiDocumentationDescriptor"/> for
/// why rewriting the prefix at the edge would break the UI instead.
/// </para>
/// </remarks>
public static class ApiDocumentationExtensions
{
    private const string LoggerCategory = "FoodDeliveryService.Documentation";

    /// <summary>
    /// Registers the service's OpenAPI document: its identity from <paramref name="descriptor"/>,
    /// the bearer scheme, each operation's permission, and the RFC 7807 failure responses.
    /// </summary>
    public static IServiceCollection AddApiDocumentation(
        this IServiceCollection services,
        IConfiguration configuration,
        ApiDocumentationDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(descriptor);

        var options = new ApiDocumentationOptions();

        configuration.GetSection(ApiDocumentationOptions.SectionName).Bind(options);

        services.AddSingleton(options);
        services.AddSingleton(descriptor);

        if (!options.Enabled)
        {
            return services;
        }

        services.AddOpenApi(ApiDocumentationDescriptor.DocumentName, openApi =>
        {
            openApi.AddDocumentTransformer(new ApiDocumentTransformer(descriptor, options));
            openApi.AddOperationTransformer(new AuthorizationOperationTransformer());
            openApi.AddOperationTransformer(new ProblemResponseOperationTransformer());
        });

        return services;
    }

    /// <summary>
    /// Maps the document and the two UIs. Call it <b>after</b> <c>UseAuthentication()</c>: the
    /// authorization gate below reads <c>HttpContext.User</c>, which is empty before it.
    /// </summary>
    /// <param name="app">The host.</param>
    /// <param name="allowAnonymous">
    /// Pass <c>app.Environment.IsDevelopment()</c>. Anywhere else the surface requires a token — see
    /// <see cref="ApiDocumentationAuthorizationMiddleware"/> for why authentication rather than a
    /// permission. The flag is an argument rather than an environment check inside this method for
    /// the same reason <c>allowInMemoryCacheFallback</c> is: the relaxation is visible in the host's
    /// own <c>Program.cs</c>, where a reviewer reads it.
    /// </param>
    public static WebApplication MapApiDocumentation(this WebApplication app, bool allowAnonymous)
    {
        ArgumentNullException.ThrowIfNull(app);

        var descriptor = app.Services.GetRequiredService<ApiDocumentationDescriptor>();
        var options = app.Services.GetRequiredService<ApiDocumentationOptions>();

        ILogger logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);

        if (!options.Enabled)
        {
            logger.LogInformation(
                "API documentation is disabled — neither the OpenAPI document nor the UIs are mapped at {Prefix}.",
                descriptor.PathPrefix);

            return app;
        }

        if (allowAnonymous)
        {
            logger.LogWarning(
                "API documentation at {Prefix} is ANONYMOUS. Expected in Development; anywhere else it " +
                "publishes every route, parameter and permission of this service.",
                descriptor.PathPrefix);
        }
        else
        {
            app.UseMiddleware<ApiDocumentationAuthorizationMiddleware>(descriptor);
        }

        app.MapOpenApi(descriptor.OpenApiRoutePattern);

        // Swashbuckle's UI is middleware rather than an endpoint, so it cannot carry authorization
        // metadata of its own — which is the second reason the gate above is a middleware and not a
        // `.RequireAuthorization()` on the endpoints.
        app.UseSwaggerUI(ui =>
        {
            ui.RoutePrefix = descriptor.SwaggerRoutePrefix;
            ui.DocumentTitle = descriptor.Title;
            ui.SwaggerEndpoint(descriptor.OpenApiDocumentPath, descriptor.Title);
        });

        // Scalar serves its own bundle (`scalar.js`, ~3.7 MB) and every asset it references from
        // this host, with relative URLs rooted at the UI's own path. That is what lets the
        // documentation CSP carve-out Milestone D shipped stay at `default-src 'self'` — a
        // CDN-hosted bundle, which some versions of this package default to, would be blocked by it
        // and the page would render blank rather than erroring. If a future bump reintroduces a CDN
        // default, pin it back with `WithBundleUrl` rather than widening the CSP.
        app.MapScalarApiReference(descriptor.ScalarPath, scalar => scalar
            .WithTitle(descriptor.Title)
            .WithOpenApiRoutePattern(descriptor.OpenApiRoutePattern));

        // A bare `/docs/{slug}` is the URL a person types (and the one `docs/api-documentation.md`
        // prints); send it to the presentable UI rather than to a 404.
        app.MapGet(descriptor.PathPrefix, () => TypedResults.Redirect($"{descriptor.ScalarPath}/"))
            .ExcludeFromDescription();

        return app;
    }
}
