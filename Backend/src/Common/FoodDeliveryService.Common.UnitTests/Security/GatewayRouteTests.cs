using System.Text.Json;
using AwesomeAssertions;
using YamlDotNet.RepresentationModel;

namespace FoodDeliveryService.Common.UnitTests.Security;

/// <summary>
/// Feature 3.7 Milestone A. The Gateway is the platform's only public entry point, so its routing
/// table is also its security boundary: authentication, authorization and the edge rate limiter all
/// hang off a YARP route. Two failure modes follow, and both have happened here before.
/// <para>
/// A route naming a cluster that is not defined is the bug <c>KUBERNETES_PHASE2_PLAN.md</c> A0 found
/// breaking <c>users/register</c> — YARP loads the rest of the configuration happily and that one
/// path 404s. And a module <em>without</em> a route is worse: it is reachable only on its container
/// port, which puts it outside the gateway's JWT validation and outside the rate limiter, quietly
/// breaking hard rule #10.
/// </para>
/// <para>
/// Nothing here builds a host. The routing table is pure configuration, so these tests read the
/// files the Gateway reads — including the Kubernetes copy, which is a hand-maintained duplicate and
/// therefore the one most likely to drift.
/// </para>
/// </summary>
public class GatewayRouteTests
{
    /// <summary>
    /// Every path prefix the platform serves. A new module host must appear here, which is the
    /// review prompt: adding the route is the step that is easy to forget, because everything works
    /// locally when you call the service on its own port.
    /// </summary>
    private static readonly string[] RequiredPathPrefixes =
    [
        "orders/",
        "restaurants/",
        "users/",
        "notifications/",
        "delivery/",
        "support/",
        "hubs/"
    ];

    /// <summary>
    /// The only two routes allowed to skip authentication at the edge, matching the anonymous
    /// endpoint allow-list in <see cref="EndpointAuthorizationTests"/>. The two lists are separate on
    /// purpose: the gateway policy and the endpoint metadata are enforced by different components,
    /// and an anonymous route in front of an authorized endpoint (or the reverse) is exactly the
    /// mismatch worth failing on.
    /// </summary>
    private static readonly string[] AnonymousPaths =
    [
        "users/register",
        "users/accept-invitation"
    ];

    private const string AnonymousPolicy = "anonymous";

    /// <summary>
    /// The Gateway's routing lives in <c>appsettings.Development.json</c> for compose and in the
    /// <c>gateway-appsettings</c> ConfigMap (as <c>appsettings.Kubernetes.json</c>) for the cluster —
    /// the base <c>appsettings.json</c> ships an empty section deliberately. Both are asserted, and
    /// <see cref="RoutingCopies_Should_NotHaveDrifted"/> keeps them the same table.
    /// </summary>
    public static TheoryData<string> RoutingConfigurations() =>
    [
        Development,
        Kubernetes
    ];

    private const string Development = "appsettings.Development.json";
    private const string Kubernetes = "gateway.yaml (appsettings.Kubernetes.json)";

    [Fact]
    public void BaseSettings_Should_ShipAnEmptyRoutingTable()
    {
        // The base file is the one that goes to every environment, and an environment-specific
        // destination address baked into it would be proxied to from environments it does not exist
        // in. Empty here is the design, not an omission — hence the assertion.
        using JsonDocument document = ReadJson(
            RepositoryPaths.Backend("src", "API", "FoodDeliveryService.Gateway", "appsettings.json"));

        JsonElement proxy = document.RootElement.GetProperty("ReverseProxy");

        proxy.GetProperty("Routes").EnumerateObject().Should().BeEmpty();
        proxy.GetProperty("Clusters").EnumerateObject().Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(RoutingConfigurations))]
    public void EveryRoute_Should_NameADefinedCluster(string configuration)
    {
        // Arrange
        RoutingTable routing = Routing(configuration);

        // Assert — an undefined cluster is a silent 404 on one path while every other route works.
        routing.Routes.Should().NotBeEmpty();

        foreach (Route route in routing.Routes)
        {
            routing.Clusters.Keys.Should().Contain(
                route.ClusterId,
                "route '{0}' in {1} forwards to cluster '{2}'",
                route.Name,
                configuration,
                route.ClusterId);
        }
    }

    [Theory]
    [MemberData(nameof(RoutingConfigurations))]
    public void EveryCluster_Should_HaveAReachableDestination(string configuration)
    {
        RoutingTable routing = Routing(configuration);

        routing.Clusters.Should().NotBeEmpty();

        foreach ((string cluster, IReadOnlyList<string> destinations) in routing.Clusters)
        {
            destinations.Should().NotBeEmpty("cluster '{0}' in {1} has no destination", cluster, configuration);

            destinations.Should().OnlyContain(
                address => Uri.IsWellFormedUriString(address, UriKind.Absolute),
                "every destination of '{0}' in {1} must be an absolute address",
                cluster,
                configuration);
        }
    }

    [Theory]
    [MemberData(nameof(RoutingConfigurations))]
    public void EveryRoute_Should_CarryAnAuthorizationPolicy(string configuration)
    {
        RoutingTable routing = Routing(configuration);

        // A YARP route with no AuthorizationPolicy is proxied without the gateway evaluating
        // anything — the request reaches the service on the strength of having arrived.
        routing.Routes.Should().OnlyContain(
            route => !string.IsNullOrWhiteSpace(route.AuthorizationPolicy),
            "{0}",
            configuration);
    }

    [Theory]
    [MemberData(nameof(RoutingConfigurations))]
    public void AnonymousRoutes_Should_BeExactlyTheTwoRegistrationPaths(string configuration)
    {
        RoutingTable routing = Routing(configuration);

        IEnumerable<string> anonymous = routing.Routes
            .Where(route => string.Equals(route.AuthorizationPolicy, AnonymousPolicy, StringComparison.Ordinal))
            .Select(route => route.Path);

        // Both directions matter: a third anonymous route is an unauthenticated hole, and a missing
        // one locks out registration for people who have no token yet by definition.
        anonymous.Should().BeEquivalentTo(AnonymousPaths, "{0}", configuration);
    }

    [Theory]
    [MemberData(nameof(RoutingConfigurations))]
    public void EveryModulePathPrefix_Should_HaveARoute(string configuration)
    {
        RoutingTable routing = Routing(configuration);

        foreach (string prefix in RequiredPathPrefixes)
        {
            routing.Routes.Should().Contain(
                route => route.Path.StartsWith(prefix, StringComparison.Ordinal),
                "{0} has no route for '{1}' — a service reachable only on its container port is " +
                "outside the gateway's authentication and rate limiting",
                configuration,
                prefix);
        }
    }

    [Fact]
    public void RoutingCopies_Should_NotHaveDrifted()
    {
        // Arrange — the ConfigMap is a hand-maintained copy of the Development table with the
        // destinations re-pointed at Kubernetes Service names, and gateway.yaml says so in a comment
        // that asks the next author to edit both places. This is that comment, enforced.
        RoutingTable development = Routing(Development);
        RoutingTable kubernetes = Routing(Kubernetes);

        // Assert — the addresses legitimately differ (compose DNS versus Service names), so what is
        // compared is the routing decision: which path, under which policy, to which cluster.
        kubernetes.Routes.Should().BeEquivalentTo(development.Routes);
        kubernetes.Clusters.Keys.Should().BeEquivalentTo(development.Clusters.Keys);
    }

    private static RoutingTable Routing(string configuration) => configuration switch
    {
        Development => Parse(ReadJson(RepositoryPaths.Backend(
            "src", "API", "FoodDeliveryService.Gateway", "appsettings.Development.json"))),
        Kubernetes => Parse(JsonDocument.Parse(KubernetesAppSettings())),
        _ => throw new ArgumentOutOfRangeException(nameof(configuration), configuration, "Unknown routing configuration.")
    };

    /// <summary>
    /// Pulls the <c>appsettings.Kubernetes.json</c> block scalar out of the <c>gateway-appsettings</c>
    /// ConfigMap. Parsed as YAML rather than sliced out of the text, so a manifest that stops being
    /// valid YAML — or a ConfigMap that is renamed — fails here rather than matching nothing.
    /// </summary>
    private static string KubernetesAppSettings()
    {
        var yaml = new YamlStream();

        using (var reader = new StreamReader(
            RepositoryPaths.Backend("deploy", "k8s", "services", "gateway.yaml")))
        {
            yaml.Load(reader);
        }

        YamlMappingNode configMap = yaml.Documents
            .Select(document => document.RootNode)
            .OfType<YamlMappingNode>()
            .Single(node => Scalar(node, "kind") == "ConfigMap");

        var data = (YamlMappingNode)configMap.Children["data"];

        return ((YamlScalarNode)data.Children["appsettings.Kubernetes.json"]).Value!;
    }

    private static string? Scalar(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(key, out YamlNode? value) ? (value as YamlScalarNode)?.Value : null;

    private static RoutingTable Parse(JsonDocument document)
    {
        using (document)
        {
            JsonElement proxy = document.RootElement.GetProperty("ReverseProxy");

            List<Route> routes =
            [
                .. proxy.GetProperty("Routes").EnumerateObject().Select(route => new Route(
                    route.Name,
                    route.Value.GetProperty("Match").GetProperty("Path").GetString()!,
                    route.Value.GetProperty("ClusterId").GetString()!,
                    route.Value.TryGetProperty("AuthorizationPolicy", out JsonElement policy)
                        ? policy.GetString()
                        : null))
            ];

            var clusters = proxy.GetProperty("Clusters")
                .EnumerateObject()
                .ToDictionary(
                    cluster => cluster.Name,
                    cluster => (IReadOnlyList<string>)
                    [
                        .. cluster.Value.GetProperty("Destinations").EnumerateObject()
                            .Select(destination => destination.Value.GetProperty("Address").GetString()!)
                    ],
                    StringComparer.Ordinal);

            return new RoutingTable(routes, clusters);
        }
    }

    private static JsonDocument ReadJson(string path) =>
        JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

    private sealed record Route(string Name, string Path, string ClusterId, string? AuthorizationPolicy);

    private sealed record RoutingTable(
        IReadOnlyList<Route> Routes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Clusters);
}
