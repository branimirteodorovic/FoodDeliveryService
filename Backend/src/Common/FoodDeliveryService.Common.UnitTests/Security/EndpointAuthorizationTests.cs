using System.Reflection;
using AwesomeAssertions;
using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Health;
using FoodDeliveryService.Modules.Users.Domain.Users;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using DeliveryApplication = FoodDeliveryService.Modules.Delivery.Application;
using DeliveryPresentation = FoodDeliveryService.Modules.Delivery.Presentation;
using NotificationsPresentation = FoodDeliveryService.Modules.Notifications.Presentation;
using OrdersApplication = FoodDeliveryService.Modules.Orders.Application;
using OrdersPresentation = FoodDeliveryService.Modules.Orders.Presentation;
using RealTimeApplication = FoodDeliveryService.Modules.RealTime.Application;
using RealTimePresentation = FoodDeliveryService.Modules.RealTime.Presentation;
using RestaurantsApplication = FoodDeliveryService.Modules.Restaurants.Application;
using RestaurantsPresentation = FoodDeliveryService.Modules.Restaurants.Presentation;
using SupportApplication = FoodDeliveryService.Modules.Support.Application;
using SupportPresentation = FoodDeliveryService.Modules.Support.Presentation;
using UsersPresentation = FoodDeliveryService.Modules.Users.Presentation;

namespace FoodDeliveryService.Common.UnitTests.Security;

/// <summary>
/// Feature 3.7 Milestone A. Every endpoint on the platform carries either a permission policy or an
/// explicit <c>AllowAnonymous</c> — today, because nobody has broken it. That is a property held up
/// by review attention, and review attention is what a sixty-endpoint surface eventually runs out
/// of: the endpoint that ships without <c>.RequireAuthorization(...)</c> looks exactly like the one
/// that has it, right up until it serves somebody else's order to an unauthenticated caller.
/// <para>
/// So this suite builds each module's <em>real</em> route table — the same
/// <see cref="EndpointExtensions.AddEndpoints"/> reflection the hosts use, then
/// <see cref="EndpointExtensions.MapEndpoints"/> against a real <see cref="WebApplication"/> — and
/// asserts the property from the endpoint metadata. A new anonymous endpoint, or a policy string
/// naming a permission the Users module never seeds, now fails the build instead of surfacing as a
/// 403 (or a 200) at runtime.
/// </para>
/// <para>
/// The route table is the only honest source here: the endpoints are <c>internal sealed</c>, so a
/// naive <c>GetExportedTypes()</c> scan finds nothing and every assertion below would pass over an
/// empty collection. <see cref="Module_Should_ExposeItsEndpoints_ThroughReflectionDiscovery"/> pins
/// the count first for exactly that reason.
/// </para>
/// </summary>
public class EndpointAuthorizationTests
{
    /// <summary>
    /// The complete anonymous surface of the platform's module hosts, by route pattern.
    /// <para>
    /// Needing to edit this list is the point of it: an anonymous endpoint cannot arrive in a
    /// different pull request from its exemption, so there is always a reviewer looking at this file
    /// asking why. <c>users/register</c> is customer self-registration (the handler forces the role
    /// to Customer) and <c>users/accept-invitation</c> is redeemed with a single-use token that is
    /// itself the credential — neither can require a token the caller does not have yet.
    /// </para>
    /// <para>
    /// The three health probes are anonymous too, but they are mapped by <c>MapHealthProbes</c>
    /// rather than discovered as <see cref="IEndpoint"/>s, so they get their own allow-list in
    /// <see cref="HealthProbes_Should_BeAnonymous_AndNothingElseThere"/>.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> AnonymousRoutes =
    [
        "users/register",
        "users/accept-invitation"
    ];

    /// <summary>
    /// Endpoints authorized as "any authenticated principal" — <c>RequireAuthorization()</c> with no
    /// policy — rather than by a permission code.
    /// <para>
    /// The SignalR tracking hub is the one place where that is right: the hub derives its group
    /// membership from the caller's permission claims after the handshake (Feature 2.2 Milestone D),
    /// so a permission policy on negotiate would gate the connection on a single role when
    /// customers, drivers, managers and agents all legitimately connect. <c>MapHub</c> contributes
    /// two endpoints — the negotiate POST and the transport route — so both are listed.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> AuthenticatedOnlyRoutes =
    [
        "hubs/tracking",
        "hubs/tracking/negotiate"
    ];

    /// <summary>
    /// Every permission code the Users module seeds. A <c>RequireAuthorization("orders:raed")</c>
    /// compiles, maps, and then denies every request with a 403 indistinguishable from a legitimate
    /// one — this is the set that catches it at build time instead.
    /// </summary>
    private static readonly HashSet<string> SeededPermissionCodes =
    [
        .. typeof(Permission)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(Permission))
            .Select(field => ((Permission)field.GetValue(null)!).Code)
    ];

    /// <summary>
    /// The seven module Presentation assemblies, with whether the module is expected to have an HTTP
    /// surface at all. Notifications is deliberately <c>false</c>: it is a pure consumer that reacts
    /// to integration events and sends email, and it has never exposed an endpoint. Written down so
    /// that "no endpoints found" reads as a decision rather than as a broken test.
    /// </summary>
    private static readonly ModuleSurface[] ModuleSurfaces =
    [
        new("Delivery", DeliveryPresentation.AssemblyReference.Assembly, HasHttpSurface: true),
        new("Notifications", NotificationsPresentation.AssemblyReference.Assembly, HasHttpSurface: false),
        new("Orders", OrdersPresentation.AssemblyReference.Assembly, HasHttpSurface: true),
        new("RealTime", RealTimePresentation.AssemblyReference.Assembly, HasHttpSurface: true),
        new("Restaurants", RestaurantsPresentation.AssemblyReference.Assembly, HasHttpSurface: true),
        new("Support", SupportPresentation.AssemblyReference.Assembly, HasHttpSurface: true),
        new("Users", UsersPresentation.AssemblyReference.Assembly, HasHttpSurface: true)
    ];

    /// <summary>
    /// The <c>Permissions</c> constant sets, one per module that has any. Referenced as types rather
    /// than looked up by name so that renaming the class is a compile error here instead of a test
    /// that quietly starts checking nothing. Users declares none (its two endpoints are anonymous)
    /// and Notifications has no HTTP surface at all.
    /// </summary>
    private static readonly Type[] ModulePermissionSets =
    [
        typeof(DeliveryApplication.Permissions),
        typeof(OrdersApplication.Permissions),
        typeof(RealTimeApplication.Permissions),
        typeof(RestaurantsApplication.Permissions),
        typeof(SupportApplication.Permissions)
    ];

    public static TheoryData<string> ModuleNames()
    {
        var data = new TheoryData<string>();

        foreach (ModuleSurface surface in ModuleSurfaces)
        {
            data.Add(surface.Module);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_Should_ExposeItsEndpoints_ThroughReflectionDiscovery(string module)
    {
        // Arrange
        ModuleSurface surface = Surface(module);

        // Act
        IReadOnlyList<RouteEndpoint> endpoints = RouteTableOf(surface.Assembly);

        // Assert — without this the internal endpoints could stop being discovered entirely and
        // every other test in this class would pass over an empty collection.
        if (surface.HasHttpSurface)
        {
            endpoints.Should().NotBeEmpty(
                "{0}.Presentation declares IEndpoint types that AddEndpoints must discover", module);
        }
        else
        {
            endpoints.Should().BeEmpty(
                "{0} is a pure event consumer — an endpoint here is an HTTP surface nobody decided to add",
                module);
        }
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void EveryEndpoint_Should_DeclareAuthorization_OrBeOnTheAnonymousAllowList(string module)
    {
        foreach (RouteEndpoint endpoint in RouteTableOf(Surface(module).Assembly))
        {
            string route = Route(endpoint);
            bool isAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            bool isAuthorized = endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null;

            // Neither is the failure this whole class exists for: an endpoint with no authorization
            // metadata at all is served to anyone who can reach the port.
            (isAnonymous || isAuthorized).Should().BeTrue(
                "{0} carries neither RequireAuthorization nor AllowAnonymous", route);

            if (isAnonymous)
            {
                AnonymousRoutes.Should().Contain(
                    route,
                    "{0} is anonymous — add it to AnonymousRoutes in this file, in this same pull " +
                    "request, with the reason it cannot require a token",
                    route);
            }
        }
    }

    [Fact]
    public void AnonymousSurface_Should_BeExactlyTheAllowList()
    {
        // Act — the reverse direction of the test above. An allow-list entry whose endpoint was
        // deleted or later given a permission is a stale exemption waiting to be reused by accident.
        IEnumerable<string> anonymous = ModuleSurfaces
            .SelectMany(surface => RouteTableOf(surface.Assembly))
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(Route)
            .Distinct();

        // Assert
        anonymous.Should().BeEquivalentTo(AnonymousRoutes);
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void EveryAuthorizationPolicy_Should_NameASeededPermissionCode(string module)
    {
        foreach (RouteEndpoint endpoint in RouteTableOf(Surface(module).Assembly))
        {
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                continue;
            }

            string route = Route(endpoint);

            foreach (IAuthorizeData authorization in endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>())
            {
                if (string.IsNullOrEmpty(authorization.Policy))
                {
                    // PermissionAuthorizationPolicyProvider only manufactures a permission
                    // requirement for a NAMED policy; an empty one falls through to the default
                    // "is authenticated" policy, which is a far weaker guarantee than the call site
                    // usually intends.
                    AuthenticatedOnlyRoutes.Should().Contain(
                        route,
                        "{0} authorizes any authenticated principal rather than a permission — name " +
                        "a permission, or add it to AuthenticatedOnlyRoutes with the reason",
                        route);

                    continue;
                }

                // The policy name IS the permission code: the provider turns it straight into a
                // PermissionRequirement, so an unseeded code is a permission nobody can ever hold.
                SeededPermissionCodes.Should().Contain(
                    authorization.Policy,
                    "{0} requires policy '{1}', which the Users module does not seed as a Permission",
                    route,
                    authorization.Policy);
            }
        }
    }

    [Fact]
    public void ModulePermissionConstants_Should_MatchASeededPermissionCode()
    {
        // Arrange — every module keeps its own `Permissions` constants, and the comment above each
        // set says they mirror the rows Users seeds. Nothing checked that they still do, and an
        // unused-but-wrong constant is the one that gets copied onto the next endpoint.
        var constants = ModulePermissionSets
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
            .Select(field => new
            {
                Declared = $"{field.DeclaringType!.FullName}.{field.Name}",
                Code = (string)field.GetRawConstantValue()!
            })
            .ToList();

        // Assert — the emptiness guard is the same vacuity trap as everywhere else in this file.
        constants.Should().NotBeEmpty("every module in ModulePermissionSets declares permission codes");

        constants.Should().OnlyContain(
            constant => SeededPermissionCodes.Contains(constant.Code),
            "a module constant naming a code Users never seeds is a permission nobody can hold");
    }

    [Fact]
    public void HealthProbes_Should_BeAnonymous_AndNothingElseThere()
    {
        // Arrange — the probes are anonymous on purpose: a kubelet carries no token. They are mapped
        // by MapHealthProbes rather than discovered, so this is where they get their allow-list.
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddHealthChecks();

        WebApplication app = builder.Build();
        app.MapHealthProbes();

        // Act
        IReadOnlyList<RouteEndpoint> endpoints = RouteTable(app);

        // Assert
        endpoints.Select(Route).Should().BeEquivalentTo(
            HealthProbeEndpointExtensions.LivenessPath,
            HealthProbeEndpointExtensions.ReadinessPath,
            HealthProbeEndpointExtensions.HealthPath);

        endpoints.Should().OnlyContain(
            endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null,
            "a probe that inherits the host's authorization default answers the kubelet with a 401");
    }

    private static ModuleSurface Surface(string module) =>
        ModuleSurfaces.Single(surface => surface.Module == module);

    /// <summary>
    /// Builds the module's route table the way its host does: discover the <see cref="IEndpoint"/>
    /// implementations by reflection, then let each one map itself. Nothing is stubbed — these are
    /// the same <see cref="RouteEndpoint"/> instances the host would serve, metadata and all.
    /// </summary>
    private static IReadOnlyList<RouteEndpoint> RouteTableOf(Assembly presentationAssembly)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();

        builder.Services.AddEndpoints(presentationAssembly);
        builder.Services.AddAuthorization();

        // Minimal API infers any handler parameter it cannot find in DI as the request BODY, and a
        // route with an inferred body alongside a route value or a second inferred body throws while
        // the route table is being built — so the table cannot be read at all without these. Only
        // the registration matters: metadata inference asks whether the type IS a service and never
        // resolves one. An endpoint that injects something new fails here with that same
        // "Body was inferred" exception, and the fix is one more line below.
        RegisterInjectedService<ISender>(builder.Services);
        RegisterInjectedService<IDateTimeProvider>(builder.Services);

        // RealTime's tracking hub self-registers through MapHub, which throws at map time without
        // the SignalR services — the one dependency an endpoint's *mapping* has here.
        builder.Services.AddSignalR();

        WebApplication app = builder.Build();
        app.MapEndpoints();

        return RouteTable(app);
    }

    private static void RegisterInjectedService<TService>(IServiceCollection services)
        where TService : class =>
        services.AddSingleton<TService>(_ => throw new NotSupportedException(
            "The route table is built for its metadata only — no request is ever handled."));

    private static IReadOnlyList<RouteEndpoint> RouteTable(WebApplication app) =>
    [
        .. ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
    ];

    private static string Route(RouteEndpoint endpoint) =>
        endpoint.RoutePattern.RawText!.TrimStart('/');

    private sealed record ModuleSurface(string Module, Assembly Assembly, bool HasHttpSurface);
}
