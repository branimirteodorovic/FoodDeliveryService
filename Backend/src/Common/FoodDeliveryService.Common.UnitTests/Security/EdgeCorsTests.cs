using AwesomeAssertions;
using FoodDeliveryService.Common.Presentation.Security;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FoodDeliveryService.Common.UnitTests.Security;

/// <summary>
/// Feature 3.7 Milestone D §5.3. CORS is the one control in this milestone that a browser enforces
/// rather than the server, so the failure modes are asymmetric: too strict and the SPA cannot call
/// the platform at all, too loose and any page on the internet can call it with the user's
/// credentials. Both halves are asserted here, on the built policy rather than on the options.
/// </summary>
public class EdgeCorsTests
{
    [Fact]
    public void AddEdgeCors_Should_BuildThePolicyFromConfiguration()
    {
        // Arrange
        CorsPolicy policy = BuildPolicy(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "https://app.fooddeliveryservice.com",
            ["Cors:AllowedOrigins:1"] = "http://localhost:4200",
            ["Cors:PreflightMaxAgeSeconds"] = "300"
        });

        // Assert
        policy.Origins.Should().Equal("https://app.fooddeliveryservice.com", "http://localhost:4200");
        policy.AllowAnyOrigin.Should().BeFalse();
        policy.AllowAnyHeader.Should().BeTrue();
        policy.AllowAnyMethod.Should().BeTrue();
        policy.PreflightMaxAge.Should().Be(TimeSpan.FromSeconds(300));
    }

    [Fact]
    public void AddEdgeCors_Should_AllowCredentialsAndExposeTheTwoHeadersTheSpaActsOn()
    {
        // Arrange — credentials, because the SignalR handshake on the hubs routes carries the
        // access token. Exposed headers, because without them a cross-origin caller can read neither
        // the correlation id it should quote in a bug report nor the Retry-After the edge limiter
        // sends with a 429, and both are things the platform expects a client to act on.
        CorsPolicy policy = BuildPolicy(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "http://localhost:4200"
        });

        // Assert
        policy.SupportsCredentials.Should().BeTrue();
        policy.ExposedHeaders.Should().BeEquivalentTo("X-Correlation-Id", "Retry-After");
    }

    [Fact]
    public void AddEdgeCors_Should_ReplaceTheDefaultExposedHeaders_NotAppendToThem()
    {
        // Arrange — the binder gotcha this milestone found by failing: Bind() *appends* to an array
        // property that already holds values, so a deployment narrowing a defaulted list would
        // silently keep the defaults alongside its own. Both non-empty defaults in this milestone go
        // through ConfiguredArray for that reason.
        CorsPolicy policy = BuildPolicy(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "http://localhost:4200",
            ["Cors:ExposedHeaders:0"] = "X-Correlation-Id"
        });

        // Assert
        policy.ExposedHeaders.Should().ContainSingle().Which.Should().Be("X-Correlation-Id");
    }

    [Fact]
    public void AddEdgeCors_Should_MatchNothing_WhenNoOriginIsConfigured()
    {
        // Arrange — the state the base appsettings.json ships, and it is the right default: a
        // configuration file that goes to every environment must not decide which browsers may
        // talk to the platform. Server-to-server callers are unaffected either way.
        CorsPolicy policy = BuildPolicy([]);

        // Assert
        policy.Origins.Should().BeEmpty();
        policy.AllowAnyOrigin.Should().BeFalse();
    }

    [Fact]
    public void AddEdgeCors_Should_RefuseAWildcardOriginWithCredentials()
    {
        // Arrange — the combination the CORS spec forbids. ASP.NET Core throws when the policy is
        // evaluated, which for a misconfigured deployment means a 500 on someone's first login.
        // Refusing it at startup with the offending key named is the difference between a
        // five-second fix and an afternoon.
        var services = new ServiceCollection();

        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "*",
            ["Cors:AllowCredentials"] = "true"
        });

        // Act
        Action act = () => services.AddEdgeCors(configuration);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cors:AllowedOrigins*AllowCredentials*");
    }

    [Fact]
    public void UseEdgeCors_Should_Fail_WhenAddEdgeCorsWasNotCalled()
    {
        // Arrange — the pair is easy to half-apply, and a missing Use is invisible while a missing
        // Add would otherwise surface as "the policy exists but is never applied".
        var services = new ServiceCollection();

        services.AddLogging();

        using ServiceProvider provider = services.BuildServiceProvider();

        var app = new Microsoft.AspNetCore.Builder.ApplicationBuilder(provider);

        // Act
        Action act = () => app.UseEdgeCors();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*AddEdgeCors*");
    }

    private static CorsPolicy BuildPolicy(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();

        services.AddEdgeCors(Configuration(settings));

        using ServiceProvider provider = services.BuildServiceProvider();

        CorsPolicy? policy = provider
            .GetRequiredService<IOptions<CorsOptions>>()
            .Value
            .GetPolicy(EdgeCorsOptions.PolicyName);

        policy.Should().NotBeNull();

        return policy;
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
