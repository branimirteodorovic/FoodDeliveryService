using AwesomeAssertions;
using FoodDeliveryService.Common.Presentation.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FoodDeliveryService.Common.UnitTests.Security;

/// <summary>
/// Feature 3.7 Milestone D §5.4. Two properties carry the milestone and both are easy to get wrong
/// in a way nothing notices: the headers must be on <b>every</b> response — a middleware that only
/// decorates the 200s is the common miss, and the error responses are the ones a scanner reads —
/// and HSTS must be absent over plain HTTP, because nothing in this repository terminates TLS and a
/// browser that honoured it would pin itself to a scheme the local platform does not serve.
/// </summary>
public class SecurityHeadersTests
{
    private const string ApiPolicy = "default-src 'none'; frame-ancestors 'none'";

    [Fact]
    public async Task Invoke_Should_StampEveryHeader_OnASuccessfulResponse()
    {
        // Arrange
        HttpContext context = CreateContext("/orders");

        // Act
        await InvokeAsync(context, _ => Task.CompletedTask);

        IHeaderDictionary headers = await StartResponseAsync(context);

        // Assert
        headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        headers["X-Frame-Options"].ToString().Should().Be("DENY");
        headers["Referrer-Policy"].ToString().Should().Be("no-referrer");
        headers["Content-Security-Policy"].ToString().Should().Be(ApiPolicy);
    }

    [Fact]
    public async Task Invoke_Should_StampTheHeaders_OnAProblemResponse()
    {
        // Arrange — the ApiResults.Problem path. A 400 carries a body a browser will render if it is
        // sniffed into HTML, so it needs the headers at least as much as a 200 does.
        HttpContext context = CreateContext("/orders/00000000-0000-0000-0000-000000000000");

        // Act
        await InvokeAsync(context, ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            ctx.Response.ContentType = "application/problem+json";

            return Task.CompletedTask;
        });

        IHeaderDictionary headers = await StartResponseAsync(context);

        // Assert
        headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        headers["Content-Security-Policy"].ToString().Should().Be(ApiPolicy);
    }

    [Fact]
    public async Task Invoke_Should_StampTheHeaders_WhenTheResponseIsResetDownstream()
    {
        // Arrange — what GlobalExceptionHandler does: throw away whatever was written and start a
        // 500 from scratch. Headers set on the way IN are lost here, which is exactly why the
        // middleware writes from OnStarting instead. This test is the reason that choice exists.
        HttpContext context = CreateContext("/orders");

        // Act
        await InvokeAsync(context, ctx =>
        {
            ctx.Response.Headers.Clear();
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;

            return Task.CompletedTask;
        });

        IHeaderDictionary headers = await StartResponseAsync(context);

        // Assert
        headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        headers["Content-Security-Policy"].ToString().Should().Be(ApiPolicy);
    }

    [Fact]
    public async Task Invoke_Should_NotEmitHsts_OverPlainHttp()
    {
        // Arrange — docker-compose and the KinD manifests are HTTP-only by design.
        HttpContext context = CreateContext("/orders");

        // Act
        await InvokeAsync(context, _ => Task.CompletedTask);

        // Assert
        (await StartResponseAsync(context)).Should().NotContainKey("Strict-Transport-Security");
    }

    [Fact]
    public async Task Invoke_Should_EmitHsts_OverHttps()
    {
        // Arrange — the deployed shape: a TLS-terminating proxy in front, whose X-Forwarded-Proto
        // the Gateway trusts (§5.2), so IsHttps is true here even though Kestrel spoke HTTP.
        HttpContext context = CreateContext("/orders");

        context.Request.Scheme = "https";

        // Act
        await InvokeAsync(context, _ => Task.CompletedTask);

        // Assert
        (await StartResponseAsync(context))["Strict-Transport-Security"].ToString()
            .Should().Be("max-age=31536000; includeSubDomains");
    }

    [Theory]
    [InlineData("/swagger")]
    [InlineData("/swagger/index.html")]
    [InlineData("/scalar/v1")]
    [InlineData("/openapi/v1.json")]
    public async Task Invoke_Should_ServeAPermissivePolicy_OnTheDocumentationPaths(string path)
    {
        // Arrange — Swagger UI and Scalar bootstrap from an inline script and inline styles. Under
        // the API policy they render blank, which looks exactly like a broken build. Milestone G
        // maps those UIs; the carve-out ships first so it never breaks in the PR that adds them.
        HttpContext context = CreateContext(path);

        // Act
        await InvokeAsync(context, _ => Task.CompletedTask);

        // Assert
        string policy = (await StartResponseAsync(context))["Content-Security-Policy"].ToString();

        policy.Should().Contain("script-src 'self' 'unsafe-inline'");
        policy.Should().Contain("style-src 'self' 'unsafe-inline'");

        // Still unframable — the carve-out is about what the page may load, not who may embed it.
        policy.Should().Contain("frame-ancestors 'none'");
    }

    [Theory]
    [InlineData("/orders")]
    [InlineData("/swaggerish")]
    [InlineData("/restaurants/scalar")]
    public async Task Invoke_Should_ServeTheStrictPolicy_EverywhereElse(string path)
    {
        // Arrange — the carve-out is a prefix match, so a route that merely contains "scalar" must
        // not inherit the permissive policy.
        HttpContext context = CreateContext(path);

        // Act
        await InvokeAsync(context, _ => Task.CompletedTask);

        // Assert
        (await StartResponseAsync(context))["Content-Security-Policy"].ToString().Should().Be(ApiPolicy);
    }

    [Fact]
    public void AddSecurityHeaders_Should_SuppressKestrelsServerHeader()
    {
        // Arrange — `Server: Kestrel` is free reconnaissance, and it is the one header this
        // milestone removes rather than adds. It cannot be turned off from the pipeline, which is
        // the whole reason AddSecurityHeaders exists alongside UseSecurityHeaders.
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddSecurityHeaders(new ConfigurationBuilder().Build());

        // Act
        using ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        provider.GetRequiredService<IOptions<KestrelServerOptions>>().Value.AddServerHeader.Should().BeFalse();
    }

    [Fact]
    public void AddSecurityHeaders_Should_BindTheConfiguredPolicies()
    {
        // Arrange
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecurityHeaders:ContentSecurityPolicy"] = "default-src 'self'",
                ["SecurityHeaders:StrictTransportSecurityMaxAgeDays"] = "30",
                ["SecurityHeaders:StrictTransportSecurityIncludeSubDomains"] = "false",
                ["SecurityHeaders:DocumentationPathPrefixes:0"] = "/reference"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddOptions();
        services.AddSecurityHeaders(configuration);

        // Act
        using ServiceProvider provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<SecurityHeadersOptions>();

        // Assert — the array is replaced, not appended to, which is the binder behaviour the
        // settable-array shape was chosen for: a configured list of documentation paths must not
        // silently keep the four defaults alongside it.
        options.ContentSecurityPolicy.Should().Be("default-src 'self'");
        options.StrictTransportSecurityValue.Should().Be("max-age=2592000");
        options.DocumentationPathPrefixes.Should().ContainSingle().Which.Should().Be("/reference");
        options.IsDocumentationPath("/swagger").Should().BeFalse();
    }

    private static Task InvokeAsync(HttpContext context, RequestDelegate next) =>
        new SecurityHeadersMiddleware(next, new SecurityHeadersOptions()).Invoke(context);

    private static async Task<IHeaderDictionary> StartResponseAsync(HttpContext context)
    {
        var feature = (RecordingResponseFeature)context.Features.Get<IHttpResponseFeature>()!;

        await feature.FireOnStartingAsync();

        return context.Response.Headers;
    }

    private static DefaultHttpContext CreateContext(string path)
    {
        var context = new DefaultHttpContext();

        context.Features.Set<IHttpResponseFeature>(new RecordingResponseFeature());
        context.Request.Path = path;

        return context;
    }
}
