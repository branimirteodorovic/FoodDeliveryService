using AwesomeAssertions;
using FoodDeliveryService.Common.Presentation.Documentation;

namespace FoodDeliveryService.Common.UnitTests.Documentation;

/// <summary>
/// Feature 3.7 Milestone G. <see cref="OpenApiDocumentTests"/> proves the document is right; this
/// proves it is <b>served</b>, by every host that has one, in the same shape.
/// <para>
/// It is <see cref="Security.SecurityHeaderCoverageTests"/>'s argument applied to documentation, and
/// the failure mode is the same: a host that registers the document but never maps it starts,
/// serves traffic and answers every request — including <c>/docs/{slug}</c>, with a 404. There is no
/// runtime signal, because nothing on the platform reads its own documentation.
/// </para>
/// <para>
/// Like that suite it reads <c>Program.cs</c> as text, which is crude and is the point: the
/// alternative is booting seven hosts, each wanting PostgreSQL, Redis, RabbitMQ and Duende, to
/// observe a route.
/// </para>
/// </summary>
public class ApiDocumentationCoverageTests
{
    /// <summary>
    /// The two hosts that document nothing, and why. Neither is a module host: the Gateway is a
    /// proxy with no API of its own (it serves the seven services' documentation by forwarding it,
    /// which is <see cref="Security.GatewayRouteTests"/>' business), and Identity is Duende, whose
    /// own surface is the OIDC discovery document rather than anything this generator would produce.
    /// </summary>
    private static readonly string[] UndocumentedHosts =
    [
        "FoodDeliveryService.Gateway",
        "FoodDeliveryService.Identity"
    ];

    [Theory]
    [MemberData(nameof(DocumentedHosts))]
    public void EveryModuleHost_Should_RegisterAndMapItsApiDocumentation(string host)
    {
        // Arrange
        string program = File.ReadAllText(RepositoryPaths.Backend("src", "API", host, "Program.cs"));

        // Assert — both halves, because each fails silently on its own: the Add without the Map
        // builds a document nothing serves (which is exactly what AddSwaggerGen was doing before this
        // milestone), and the Map without the Add throws at boot.
        program.Should().Contain(
            "AddApiDocumentation(builder.Configuration, ApiDocumentation.",
            $"{host} must register its OpenAPI document from the shared descriptor");

        program.Should().Contain(
            "app.MapApiDocumentation(allowAnonymous: app.Environment.IsDevelopment());",
            $"{host} must map the document and both UIs, anonymous only in Development");
    }

    [Theory]
    [MemberData(nameof(DocumentedHosts))]
    public void TheDocumentation_Should_BeMappedAfterAuthentication(string host)
    {
        // Arrange
        string program = File.ReadAllText(RepositoryPaths.Backend("src", "API", host, "Program.cs"));

        // Assert — not cosmetic. Outside Development the documentation is gated on the caller being
        // authenticated, and HttpContext.User is empty until UseAuthentication has run; mapped
        // earlier, the gate would reject every request including a perfectly good token.
        program.IndexOf("app.UseAuthentication();", StringComparison.Ordinal)
            .Should().BeLessThan(program.IndexOf("app.MapApiDocumentation(", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryDescribedService_Should_HaveAHostThatServesIt()
    {
        // Arrange — the reverse direction. A descriptor in ApiDocumentation.All that no host passes
        // is a documented service that does not exist: it would still get a Gateway docs route (that
        // test reads the same list), and that route would forward to nothing.
        var served = new List<string>();

        foreach (string host in DocumentedHostDirectories())
        {
            string program = File.ReadAllText(RepositoryPaths.Backend("src", "API", host, "Program.cs"));

            foreach (ApiDocumentationDescriptor descriptor in ApiDocumentation.All)
            {
                if (program.Contains(
                        $"ApiDocumentation.{DescriptorPropertyName(descriptor)});",
                        StringComparison.Ordinal))
                {
                    served.Add(descriptor.Slug);
                }
            }
        }

        // Assert
        served.Should().BeEquivalentTo(ApiDocumentation.All.Select(descriptor => descriptor.Slug));
    }

    /// <summary>
    /// The property name on <see cref="ApiDocumentation"/> for a descriptor — the slug with its
    /// first letter capitalised, except <c>realtime</c>, whose property is <c>RealTime</c>. Derived
    /// rather than listed so that a new service needs no edit here.
    /// </summary>
    private static string DescriptorPropertyName(ApiDocumentationDescriptor descriptor) =>
        typeof(ApiDocumentation)
            .GetProperties()
            .Single(property =>
                property.PropertyType == typeof(ApiDocumentationDescriptor) &&
                ReferenceEquals(property.GetValue(null), descriptor))
            .Name;

    public static TheoryData<string> DocumentedHosts()
    {
        var data = new TheoryData<string>();

        foreach (string host in DocumentedHostDirectories())
        {
            data.Add(host);
        }

        return data;
    }

    /// <summary>
    /// Every host directory under <c>src/API</c> that still holds a <c>Program.cs</c>, minus the two
    /// that document nothing. The <c>Program.cs</c> filter is what keeps the reverted FraudDetection
    /// host — whose stale build output is still on disk — out of the theory, exactly as in
    /// <see cref="Security.SecurityHeaderCoverageTests"/>.
    /// </summary>
    private static IEnumerable<string> DocumentedHostDirectories() =>
        Directory
            .EnumerateDirectories(RepositoryPaths.Backend("src", "API"))
            .Where(directory => File.Exists(Path.Combine(directory, "Program.cs")))
            .Select(Path.GetFileName)
            .Where(name => name is not null && !UndocumentedHosts.Contains(name, StringComparer.Ordinal))
            .Select(name => name!)
            .Order(StringComparer.Ordinal);
}
