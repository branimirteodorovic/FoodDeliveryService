using System.Net;
using AwesomeAssertions;
using FoodDeliveryService.Common.Presentation.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

// Microsoft.AspNetCore.HttpOverrides also defines an IPNetwork, and it is obsolete in .NET 10 —
// KnownIPNetworks is typed on System.Net's. Same alias as the production file, same reason.
using IPNetwork = System.Net.IPNetwork;

namespace FoodDeliveryService.Common.UnitTests.Security;

/// <summary>
/// Feature 3.7 Milestone D §5.2. <c>X-Forwarded-For</c> is a client-supplied header, so this is the
/// one place in the milestone where the wrong default is worse than no feature at all: honouring it
/// from an untrusted sender lets any caller choose its own edge rate-limit partition key
/// (<c>RateLimitClient</c>) and its own address in the logs. The tests assert that trust is opt-in,
/// and that what a deployment opts into is exactly what it wrote.
/// </summary>
public class EdgeForwardedHeadersTests
{
    [Fact]
    public void AddEdgeForwardedHeaders_Should_TrustNothing_ByDefault()
    {
        // Arrange — the framework pre-trusts loopback (127.0.0.1/8, ::1). Cleared on purpose: a
        // sidecar or a compromised process on the same host is not a trusted proxy just because it
        // is local, and "the trust list is exactly what configuration says" is a far easier property
        // to reason about than "configuration plus two entries you did not write".
        ForwardedHeadersOptions options = Build([]);

        // Assert
        options.KnownProxies.Should().BeEmpty();
        options.KnownIPNetworks.Should().BeEmpty();
    }

    [Fact]
    public void AddEdgeForwardedHeaders_Should_ForwardOnlyTheAddressAndTheScheme()
    {
        // Arrange — Host is deliberately absent: the Gateway generates no absolute URLs, so an
        // inbound X-Forwarded-Host could only ever poison a link the platform does not send.
        ForwardedHeadersOptions options = Build([]);

        // Assert
        options.ForwardedHeaders.Should().Be(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
        options.ForwardLimit.Should().Be(1);
    }

    [Fact]
    public void AddEdgeForwardedHeaders_Should_TrustTheConfiguredProxiesAndNetworks()
    {
        // Arrange — the shape a real deployment uses: a cluster's pod CIDR (the proxy's address is
        // not stable, its network is) plus, occasionally, one fixed ingress address.
        ForwardedHeadersOptions options = Build(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownNetworks:0"] = "10.244.0.0/16",
            ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.7",
            ["ForwardedHeaders:ForwardLimit"] = "2"
        });

        // Assert
        options.KnownProxies.Should().ContainSingle().Which.Should().Be(IPAddress.Parse("10.0.0.7"));
        options.KnownIPNetworks.Should().ContainSingle().Which.Should().Be(IPNetwork.Parse("10.244.0.0/16"));
        options.ForwardLimit.Should().Be(2);
    }

    [Fact]
    public void AddEdgeForwardedHeaders_Should_NameTheOffendingKey_WhenANetworkIsNotCidr()
    {
        // Arrange — a bare address instead of a CIDR is the natural mistake, and IPNetwork.Parse's
        // own message says nothing about which configuration key it came from.
        Action act = () => Build(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownNetworks:0"] = "10.244.0.1"
        });

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ForwardedHeaders:KnownNetworks*10.244.0.1*");
    }

    [Fact]
    public void EdgeForwardedHeadersOptions_Should_ReportWhetherAnythingIsTrusted()
    {
        // Arrange — the flag behind the startup warning. Running behind a proxy with an empty trust
        // list is invisible otherwise: everything works, the rate limiter is simply no longer per
        // client.
        var untrusting = new EdgeForwardedHeadersOptions();
        var trusting = new EdgeForwardedHeadersOptions { KnownNetworks = ["10.244.0.0/16"] };

        // Assert
        untrusting.HasTrustedUpstream.Should().BeFalse();
        trusting.HasTrustedUpstream.Should().BeTrue();
    }

    private static ForwardedHeadersOptions Build(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddEdgeForwardedHeaders(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        using ServiceProvider provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
    }
}
