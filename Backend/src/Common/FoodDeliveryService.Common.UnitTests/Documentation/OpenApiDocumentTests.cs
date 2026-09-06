using System.Collections.Concurrent;
using System.Reflection;
using AwesomeAssertions;
using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Presentation.Documentation;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Security;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using DeliveryPresentation = FoodDeliveryService.Modules.Delivery.Presentation;
using NotificationsPresentation = FoodDeliveryService.Modules.Notifications.Presentation;
using OrdersPresentation = FoodDeliveryService.Modules.Orders.Presentation;
using RealTimePresentation = FoodDeliveryService.Modules.RealTime.Presentation;
using RestaurantsPresentation = FoodDeliveryService.Modules.Restaurants.Presentation;
using SupportPresentation = FoodDeliveryService.Modules.Support.Presentation;
using UsersPresentation = FoodDeliveryService.Modules.Users.Presentation;

namespace FoodDeliveryService.Common.UnitTests.Documentation;

/// <summary>
/// Feature 3.7 Milestone G §8.4. Milestone G's real cost was the writing — a summary, a description
/// and a response type on every endpoint on the platform. Writing decays: the endpoint added next
/// month compiles, maps, serves traffic and appears in the document as an untitled entry with no
/// description and no declared responses, and nothing anywhere says so.
/// <para>
/// So the document is built here, in memory, from the same
/// <see cref="ApiDocumentationExtensions.AddApiDocumentation"/> the hosts call, and asserted. What
/// is asserted is deliberately the <em>shape</em> a reader depends on — every operation is named,
/// every operation says what it takes to call it, every operation declares what comes back — and
/// never the prose itself, which is the author's to write.
/// </para>
/// <para>
/// <b>The host has to be started.</b> A <see cref="WebApplication"/> keeps the endpoint data sources
/// <c>MapEndpoints</c> added in its own collection and only publishes them to the container when it
/// starts, and the OpenAPI document is generated from the container's. Skip
/// <see cref="IHost.StartAsync"/> and every assertion below passes over a document with zero paths —
/// the same vacuity trap <see cref="Security.EndpointAuthorizationTests"/> guards with its own count
/// check, which is why <see cref="Document_Should_DescribeTheServicesHttpSurface"/> comes first.
/// Kestrel is bound on port 0 so that starting seven hosts collides with nothing.
/// </para>
/// </summary>
public class OpenApiDocumentTests
{
    /// <summary>
    /// The seven documented services, paired with the Presentation assembly whose endpoints make up
    /// the document. Two of them produce an <em>empty</em> document, and both are decisions rather
    /// than omissions — written down here so that "no operations" cannot pass for either one.
    /// <list type="bullet">
    /// <item><b>Notifications</b> is a pure event consumer and has never exposed an endpoint, the
    /// same call <see cref="Security.EndpointAuthorizationTests"/> records.</item>
    /// <item><b>RealTime</b> has exactly one endpoint and it is a SignalR hub, which the API explorer
    /// does not describe — <c>MapHub</c> contributes no <c>ApiDescription</c>, so no amount of
    /// <c>WithSummary</c> on it reaches an OpenAPI document. Its metadata is still written (see
    /// <c>TrackingHubEndpoint</c>); it is read by people, not by this generator. What a client needs
    /// to know about the handshake is in the service description instead.</item>
    /// </list>
    /// </summary>
    private static readonly ModuleDocumentation[] Modules =
    [
        new(ApiDocumentation.Orders, OrdersPresentation.AssemblyReference.Assembly, HasDocumentedOperations: true),
        new(ApiDocumentation.Restaurants, RestaurantsPresentation.AssemblyReference.Assembly, HasDocumentedOperations: true),
        new(ApiDocumentation.Users, UsersPresentation.AssemblyReference.Assembly, HasDocumentedOperations: true),
        new(ApiDocumentation.Delivery, DeliveryPresentation.AssemblyReference.Assembly, HasDocumentedOperations: true),
        new(ApiDocumentation.Support, SupportPresentation.AssemblyReference.Assembly, HasDocumentedOperations: true),
        new(ApiDocumentation.RealTime, RealTimePresentation.AssemblyReference.Assembly, HasDocumentedOperations: false),
        new(ApiDocumentation.Notifications, NotificationsPresentation.AssemblyReference.Assembly, HasDocumentedOperations: false)
    ];

    /// <summary>
    /// The operations allowed to carry no security requirement, by <c>{METHOD} {path}</c>. The same
    /// two paths as <see cref="Security.EndpointAuthorizationTests"/>'s allow-list, and listed a
    /// second time on purpose: that one asserts the endpoint metadata, this one asserts what the
    /// document <em>tells a reader</em>, and an endpoint that is authorized but documented as
    /// anonymous is its own kind of wrong.
    /// </summary>
    private static readonly HashSet<string> AnonymousOperations =
    [
        "POST /users/register",
        "POST /users/accept-invitation"
    ];

    private static readonly ConcurrentDictionary<string, Task<OpenApiDocument>> Documents = new(StringComparer.Ordinal);

    public static TheoryData<string> Services()
    {
        var data = new TheoryData<string>();

        foreach (ModuleDocumentation module in Modules)
        {
            data.Add(module.Descriptor.Slug);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task Document_Should_DescribeTheServicesHttpSurface(string slug)
    {
        // Arrange
        ModuleDocumentation module = Module(slug);

        // Act
        OpenApiDocument document = await DocumentOf(module);

        // Assert — the emptiness guard for everything below it.
        if (module.HasDocumentedOperations)
        {
            Operations(document).Should().NotBeEmpty(
                "{0}'s endpoints must reach the OpenAPI document, not just the route table", slug);
        }
        else
        {
            Operations(document).Should().BeEmpty(
                "{0} documents no operations by decision — an operation here is an HTTP surface " +
                "nobody decided to add, or a hub that unexpectedly became describable",
                slug);
        }
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task EveryOperation_Should_CarryASummaryAndADescription(string slug)
    {
        OpenApiDocument document = await DocumentOf(Module(slug));

        foreach ((string name, OpenApiOperation operation) in Operations(document))
        {
            // The summary is the line in the sidebar. Without it the reference UI lists a verb and a
            // path, which is what the route table already told you.
            operation.Summary.Should().NotBeNullOrWhiteSpace(
                "{0} has no .WithSummary(...)", name);

            operation.Description.Should().NotBeNullOrWhiteSpace(
                "{0} has no .WithDescription(...)", name);
        }
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task EveryOperation_Should_BeTagged(string slug)
    {
        OpenApiDocument document = await DocumentOf(Module(slug));

        foreach ((string name, OpenApiOperation operation) in Operations(document))
        {
            // An untagged operation lands in a nameless group at the bottom of the sidebar, below
            // every tagged one — which is where a reader stops looking.
            operation.Tags.Should().NotBeNullOrEmpty("{0} has no .WithTags(Tags.X)", name);
        }
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task EveryAuthorizedOperation_Should_DeclareTheBearerRequirement(string slug)
    {
        OpenApiDocument document = await DocumentOf(Module(slug));

        foreach ((string name, OpenApiOperation operation) in Operations(document))
        {
            if (AnonymousOperations.Contains(name))
            {
                // Both directions, as everywhere else in this suite: an operation on the anonymous
                // list that started declaring a requirement has quietly become an endpoint the two
                // people who can currently call it can no longer call.
                operation.Security.Should().BeNullOrEmpty(
                    "{0} is on the anonymous allow-list — it must not advertise a bearer requirement",
                    name);

                continue;
            }

            operation.Security.Should().NotBeNullOrEmpty(
                "{0} requires a token; a document that does not say so sends every reader into a 401",
                name);

            operation.Security!
                .SelectMany(requirement => requirement.Keys)
                .Select(scheme => scheme.Reference?.Id)
                .Should().AllSatisfy(id => id.Should().Be(ApiDocumentTransformer.BearerSchemeName));
        }
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task EveryOperation_Should_DeclareItsSuccessAndItsFailureResponses(string slug)
    {
        OpenApiDocument document = await DocumentOf(Module(slug));

        foreach ((string name, OpenApiOperation operation) in Operations(document))
        {
            var declared = new HashSet<string>(
                operation.Responses?.Keys ?? Enumerable.Empty<string>(),
                StringComparer.Ordinal);

            // The success response comes from the endpoint's own .Produces(...) — 200 with a body,
            // 204 without. Nothing synthesizes it, so its absence is a real omission.
            declared.Should().Contain(
                code => code.StartsWith('2'),
                "{0} declares no success response — add .Produces<T>() or .Produces(StatusCodes.Status204NoContent)",
                name);

            // These three are synthesized for every operation, so a failure here is the transformer
            // having stopped running rather than an endpoint having been written carelessly.
            declared.Should().Contain("400", "{0} must document the validation failure", name);
            declared.Should().Contain("429", "{0} must document the rate-limit rejection", name);
            declared.Should().Contain("500", "{0} must document the unhandled failure", name);

            if (!AnonymousOperations.Contains(name))
            {
                declared.Should().Contain("401", "{0} requires a token, so 401 is reachable", name);
                declared.Should().Contain("403", "{0} requires a permission, so 403 is reachable", name);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task Document_Should_IdentifyItsOwnService(string slug)
    {
        ModuleDocumentation module = Module(slug);

        OpenApiDocument document = await DocumentOf(module);

        // The failure this replaces: seven services all titled "FoodDeliveryService API", described
        // as "built using the modular monolith architecture" — one string, wrong twice.
        document.Info?.Title.Should().Be(module.Descriptor.Title);
        document.Info?.Description.Should().Be(module.Descriptor.Description);
        document.Info?.Version.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task Document_Should_DeclareTheBearerSecurityScheme(string slug)
    {
        OpenApiDocument document = await DocumentOf(Module(slug));

        // Declared once as a component even for Notifications, which has no operation to reference
        // it: the scheme belongs to the service, not to the endpoints that happen to exist today.
        document.Components?.SecuritySchemes.Should().ContainKey(ApiDocumentTransformer.BearerSchemeName);

        var scheme = (OpenApiSecurityScheme)document.Components!.SecuritySchemes![ApiDocumentTransformer.BearerSchemeName];

        scheme.Type.Should().Be(SecuritySchemeType.Http);
        scheme.Scheme.Should().Be("bearer");
    }

    [Fact]
    public void EveryService_Should_HaveADistinctSlugAndTitle()
    {
        // The slug is a URL segment and the title is what a reader sees in the tab; two services
        // sharing either is the exact confusion this milestone set out to end. The slug collision
        // would be worse than confusing — two Gateway routes would claim the same path.
        ApiDocumentation.All.Select(descriptor => descriptor.Slug).Should().OnlyHaveUniqueItems();
        ApiDocumentation.All.Select(descriptor => descriptor.Title).Should().OnlyHaveUniqueItems();

        ApiDocumentation.All.Should().BeEquivalentTo(Modules.Select(module => module.Descriptor));
    }

    [Fact]
    public void EveryDocumentationPath_Should_SitUnderTheDocumentationCspCarveOut()
    {
        // Milestone D shipped the carve-out before there was a UI to serve under it (§5.1). This is
        // the other half of that bet: every path this milestone maps must actually fall inside it,
        // or Swagger UI and Scalar render blank under `default-src 'none'` — a failure that looks
        // exactly like a broken build and is one line of configuration away from being neither.
        var headers = new SecurityHeadersOptions();

        foreach (ApiDocumentationDescriptor descriptor in ApiDocumentation.All)
        {
            foreach (string path in new[]
            {
                descriptor.PathPrefix,
                descriptor.OpenApiDocumentPath,
                descriptor.ScalarPath,
                descriptor.SwaggerPath
            })
            {
                headers.IsDocumentationPath(path).Should().BeTrue(
                    "{0} is served the strict API policy, under which the UI cannot load", path);
            }
        }
    }

    private static ModuleDocumentation Module(string slug) =>
        Modules.Single(module => string.Equals(module.Descriptor.Slug, slug, StringComparison.Ordinal));

    /// <summary>
    /// Built once per service and shared: starting seven hosts once per assertion would be seven
    /// times the cost for the same document.
    /// </summary>
    private static Task<OpenApiDocument> DocumentOf(ModuleDocumentation module) =>
        Documents.GetOrAdd(module.Descriptor.Slug, _ => GenerateAsync(module));

    private static async Task<OpenApiDocument> GenerateAsync(ModuleDocumentation module)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();

        // Port 0 — the host has to start (see the class remarks) and seven of them start here.
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddEndpoints(module.Assembly);
        builder.Services.AddAuthorization();

        // The same three registrations EndpointAuthorizationTests needs, for the same reason: minimal
        // API infers an unresolvable handler parameter as the request body and then throws while the
        // route table is built. Only the registration matters — no request is ever handled.
        RegisterInjectedService<ISender>(builder.Services);
        RegisterInjectedService<IDateTimeProvider>(builder.Services);
        builder.Services.AddSignalR();

        builder.Services.AddApiDocumentation(builder.Configuration, module.Descriptor);

        WebApplication app = builder.Build();

        app.MapEndpoints();

        await app.StartAsync();

        try
        {
            return await app.Services
                .GetRequiredKeyedService<IOpenApiDocumentProvider>(ApiDocumentationDescriptor.DocumentName)
                .GetOpenApiDocumentAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static void RegisterInjectedService<TService>(IServiceCollection services)
        where TService : class =>
        services.AddSingleton<TService>(_ => throw new NotSupportedException(
            "The document is built from endpoint metadata only — no request is ever handled."));

    /// <summary>Every operation in the document, keyed the way a reader would name it.</summary>
    private static IReadOnlyList<(string Name, OpenApiOperation Operation)> Operations(OpenApiDocument document) =>
    [
        .. (document.Paths ?? [])
            .SelectMany(path => (path.Value.Operations ?? [])
                .Select(operation => (
                    Name: $"{operation.Key.ToString().ToUpperInvariant()} {path.Key}",
                    Operation: operation.Value)))
    ];

    private sealed record ModuleDocumentation(
        ApiDocumentationDescriptor Descriptor,
        Assembly Assembly,
        bool HasDocumentedOperations);
}
