using AwesomeAssertions;
using FoodDeliveryService.Common.Presentation.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace FoodDeliveryService.Common.UnitTests.RateLimiting;

/// <summary>
/// The route ranking decides what the platform gives up first when it runs out of capacity, so the
/// two ways it can be wrong are both expensive: an exempt path that stops being exempt turns the
/// blackbox exporter's probes into a false outage, and a lifecycle transition that drops out of
/// <see cref="RateLimitTier.Critical"/> means a driver can be refused permission to record a
/// delivery that has already happened.
/// </summary>
public class RateLimitRoutePolicyTests
{
    [Theory]
    // The probe contract, verbatim — `docs/health-probe-contract.md`. The blackbox exporter hits
    // these on every host every 15 s from outside the platform.
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/health")]
    // negotiate, connect, and the WebSocket that follows. One logical connection over two requests,
    // and a long-lived one — it must never occupy a concurrency slot or be counted per request.
    [InlineData("/hubs/tracking/negotiate")]
    [InlineData("/hubs/dashboard")]
    public void Classify_Should_ExemptProbesAndHubs(string path)
    {
        // Act
        RateLimitTier tier = RateLimitRoutePolicy.Classify(HttpMethods.Get, path);

        // Assert
        tier.Should().Be(RateLimitTier.Exempt);
    }

    [Fact]
    public void Classify_Should_ExemptHubsRegardlessOfMethod()
    {
        // Arrange — negotiate is a POST, and it is the half of the handshake that matters most.
        // Act
        RateLimitTier tier = RateLimitRoutePolicy.Classify(HttpMethods.Post, "/hubs/tracking/negotiate");

        // Assert
        tier.Should().Be(RateLimitTier.Exempt);
    }

    [Theory]
    // Everything a kitchen or a driver does to an order or delivery that already exists. A 429 here
    // strands work a human is standing next to.
    [InlineData("/orders/8a2e6d1c-0000-4000-8000-000000000001/accept")]
    [InlineData("/orders/8a2e6d1c-0000-4000-8000-000000000001/preparing")]
    [InlineData("/orders/8a2e6d1c-0000-4000-8000-000000000001/ready")]
    [InlineData("/orders/8a2e6d1c-0000-4000-8000-000000000001/cancel")]
    [InlineData("/delivery/deliveries/8a2e6d1c-0000-4000-8000-000000000001/accept")]
    [InlineData("/delivery/deliveries/8a2e6d1c-0000-4000-8000-000000000001/picked-up")]
    [InlineData("/delivery/deliveries/8a2e6d1c-0000-4000-8000-000000000001/delivered")]
    public void Classify_Should_RankLifecycleTransitionsCritical(string path)
    {
        // Act
        RateLimitTier tier = RateLimitRoutePolicy.Classify(HttpMethods.Post, path);

        // Assert
        tier.Should().Be(RateLimitTier.Critical);
    }

    [Theory]
    // Placing an order creates new work — a rejection costs a retry and strands nothing, which is
    // exactly why it is not Critical however important it looks.
    [InlineData("POST", "/orders")]
    [InlineData("POST", "/restaurants")]
    [InlineData("POST", "/users/register")]
    // The platform's highest-frequency write, and the one most worth being able to shed.
    [InlineData("POST", "/delivery/drivers/me/location")]
    [InlineData("PATCH", "/delivery/drivers/me/availability")]
    [InlineData("PUT", "/restaurants/8a2e6d1c-0000-4000-8000-000000000001")]
    [InlineData("DELETE", "/restaurants/8a2e6d1c-0000-4000-8000-000000000001")]
    public void Classify_Should_RankMutationsWrite(string method, string path)
    {
        // Act
        RateLimitTier tier = RateLimitRoutePolicy.Classify(method, path);

        // Assert
        tier.Should().Be(RateLimitTier.Write);
    }

    [Theory]
    [InlineData("GET", "/restaurants")]
    [InlineData("GET", "/restaurants/8a2e6d1c-0000-4000-8000-000000000001/menu")]
    [InlineData("GET", "/orders/8a2e6d1c-0000-4000-8000-000000000001")]
    [InlineData("GET", "/delivery/orders/8a2e6d1c-0000-4000-8000-000000000001/delivery")]
    [InlineData("HEAD", "/restaurants")]
    public void Classify_Should_RankReadsRead(string method, string path)
    {
        // Act
        RateLimitTier tier = RateLimitRoutePolicy.Classify(method, path);

        // Assert
        tier.Should().Be(RateLimitTier.Read);
    }

    [Theory]
    // A path that merely *starts* like an exempt one is not exempt. `healthz` and `hubsomething`
    // are the shapes a client would reach for to walk around a prefix match, and a bare `hubs` is
    // not a hub endpoint at all.
    [InlineData("/healthz")]
    [InlineData("/health-check")]
    [InlineData("/hubsy/tracking")]
    [InlineData("/hubs")]
    public void Classify_Should_NotExemptLookalikePaths(string path)
    {
        // Act
        RateLimitTier tier = RateLimitRoutePolicy.Classify(HttpMethods.Get, path);

        // Assert
        tier.Should().NotBe(RateLimitTier.Exempt);
    }

    [Theory]
    // A trailing slash is a different string and the same route. Left unhandled it is a free way
    // past a path-matched guard — here it would only lose an exemption, but the same code decides
    // the critical tier, where it would cost a stranded delivery.
    [InlineData("/health/live/", RateLimitTier.Exempt)]
    [InlineData("/restaurants/", RateLimitTier.Read)]
    public void Classify_Should_IgnoreSurroundingSlashes(string path, RateLimitTier expected)
    {
        // Act
        RateLimitTier tier = RateLimitRoutePolicy.Classify(HttpMethods.Get, path);

        // Assert
        tier.Should().Be(expected);
    }

    [Fact]
    public void Classify_Should_RankAGetOnALifecycleRouteAsARead()
    {
        // Arrange — the critical rules are POST-only on purpose: the *transition* is what must not
        // be shed. Reading the same resource is a read like any other.
        // Act
        RateLimitTier tier = RateLimitRoutePolicy.Classify(
            HttpMethods.Get,
            "/delivery/deliveries/8a2e6d1c-0000-4000-8000-000000000001");

        // Assert
        tier.Should().Be(RateLimitTier.Read);
    }

    [Fact]
    public void Classify_Should_RankAnUnknownMutationWrite()
    {
        // Arrange — the fallback that matters for every route this platform has not built yet. It
        // must land inside the limiter rather than outside it.
        // Act
        RateLimitTier tier = RateLimitRoutePolicy.Classify(HttpMethods.Post, "/reviews/some/new/thing");

        // Assert
        tier.Should().Be(RateLimitTier.Write);
    }
}
