using AwesomeAssertions;

namespace FoodDeliveryService.Common.UnitTests.Security;

/// <summary>
/// Feature 3.7 Milestone D. <see cref="SecurityHeadersTests"/> proves the middleware is right; this
/// proves it is <b>on</b>.
/// <para>
/// A header present on eight of nine hosts is a header a reviewer cannot rely on, and there is no
/// runtime signal for the missing one — the host starts, serves traffic and answers every request
/// without a CSP. The same argument produced <c>UseRequestCorrelation()</c>'s seven copies and
/// <c>SecretHygieneTests</c>' sweep, so this follows their shape: a <see cref="TheoryAttribute"/>
/// over the host directories rather than a hard-coded list, so a tenth host is covered the day it is
/// added rather than the day someone remembers.
/// </para>
/// <para>
/// It reads <c>Program.cs</c> as text, which is crude and is the point: the alternative is booting
/// nine hosts, each of which wants PostgreSQL, Redis, RabbitMQ and Duende, to observe a header.
/// </para>
/// </summary>
public class SecurityHeaderCoverageTests
{
    [Theory]
    [MemberData(nameof(Hosts))]
    public void EveryHost_Should_RegisterAndUseTheSharedSecurityHeaders(string host)
    {
        // Arrange
        string program = File.ReadAllText(RepositoryPaths.Backend("src", "API", host, "Program.cs"));

        // Assert — both halves, because they are separable and each fails silently on its own: the
        // Add without the Use leaves the pipeline bare, and the Use without the Add throws at boot
        // (which is the loud half, and the reason it throws).
        program.Should().Contain(
            "AddSecurityHeaders(builder.Configuration)",
            $"{host} must bind the shared header options and turn off Kestrel's Server header");

        program.Should().Contain(
            "app.UseSecurityHeaders();",
            $"{host} must put the shared header middleware in its pipeline");
    }

    [Fact]
    public void OnlyTheGateway_Should_TrustForwardedHeadersAndServeCors()
    {
        // Arrange — both are edge concerns, for the same reason the rate limiter is (Hard Rule 10):
        // module hosts sit behind YARP on a private network, no browser can reach them, and a second
        // hop of header rewriting there would only widen the surface. A module host that acquires
        // either of these calls has almost certainly been made publicly reachable, which is the
        // thing worth failing a build over.
        var offenders = new List<string>();

        foreach (string host in HostDirectories())
        {
            if (host is Gateway)
            {
                continue;
            }

            string program = File.ReadAllText(RepositoryPaths.Backend("src", "API", host, "Program.cs"));

            if (program.Contains("EdgeForwardedHeaders", StringComparison.Ordinal) ||
                program.Contains("EdgeCors", StringComparison.Ordinal))
            {
                offenders.Add(host);
            }
        }

        // Assert
        offenders.Should().BeEmpty("forwarded headers and CORS belong to the edge and only to the edge");
    }

    [Fact]
    public void TheGateway_Should_TrustForwardedHeadersAndServeCors()
    {
        // Arrange
        string program = File.ReadAllText(RepositoryPaths.Backend("src", "API", Gateway, "Program.cs"));

        // Assert — ordering is asserted too, and it is not cosmetic. Forwarded headers must be
        // resolved before correlation, request logging and the limiter, because each of those reads
        // the address or the scheme it rewrites; CORS must come before authentication, because a
        // preflight carries no bearer token and would otherwise be answered with a 401.
        program.Should().Contain("app.UseEdgeForwardedHeaders();");
        program.Should().Contain("app.UseEdgeCors();");

        program.IndexOf("app.UseEdgeForwardedHeaders();", StringComparison.Ordinal)
            .Should().BeLessThan(program.IndexOf("app.UseRequestCorrelation();", StringComparison.Ordinal));

        program.IndexOf("app.UseEdgeCors();", StringComparison.Ordinal)
            .Should().BeLessThan(program.IndexOf("app.UseAuthentication();", StringComparison.Ordinal));
    }

    private const string Gateway = "FoodDeliveryService.Gateway";

    public static TheoryData<string> Hosts()
    {
        var data = new TheoryData<string>();

        foreach (string host in HostDirectories())
        {
            data.Add(host);
        }

        return data;
    }

    /// <summary>
    /// Every directory under <c>src/API</c> that still holds a <c>Program.cs</c>. The filter is what
    /// keeps the reverted FraudDetection host — whose stale <c>bin/obj</c> output is still on disk
    /// and tracked by nothing — out of the theory.
    /// </summary>
    private static IEnumerable<string> HostDirectories() =>
        Directory
            .EnumerateDirectories(RepositoryPaths.Backend("src", "API"))
            .Where(directory => File.Exists(Path.Combine(directory, "Program.cs")))
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .Order(StringComparer.Ordinal);
}
