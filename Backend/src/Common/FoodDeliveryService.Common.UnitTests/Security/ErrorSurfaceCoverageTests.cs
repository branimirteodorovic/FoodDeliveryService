using AwesomeAssertions;

namespace FoodDeliveryService.Common.UnitTests.Security;

/// <summary>
/// Feature 3.7 Milestone F §7.4. <see cref="ErrorSurfaceTests"/> proves the shape of a failure body
/// that goes through <c>ApiResults.Problem</c>; this proves the two things that decide what happens
/// to the failures that never reach it — the unhandled exception, and what the database is asked to
/// put inside its own error messages.
/// <para>
/// Same shape and same reasoning as <see cref="SecurityHeaderCoverageTests"/>: a theory over the
/// host directories rather than a hard-coded list, reading <c>Program.cs</c> as text, because the
/// alternative is booting nine hosts that each want PostgreSQL, Redis, RabbitMQ and Duende in order
/// to observe one response body.
/// </para>
/// </summary>
public class ErrorSurfaceCoverageTests
{
    /// <summary>
    /// The two hosts that map no <c>IEndpoint</c> and therefore never call <c>ApiResults.Problem</c>.
    /// The Gateway's job is to proxy — YARP renders its own failures and an exception in the thin
    /// pipeline in front of it is a bug, not a response shape — and Identity's error surface is
    /// Duende's, which is out of this repository's hands. Neither is exempt from the connection
    /// string rule below.
    /// </summary>
    private static readonly string[] HostsWithoutAnApplicationErrorSurface =
        ["FoodDeliveryService.Gateway", "FoodDeliveryService.Identity"];

    /// <summary>
    /// Npgsql's <c>Include Error Detail</c> puts the offending <b>row values</b> into the exception
    /// message: the duplicate key, the failing constraint's data. In Development that is exactly what
    /// you want. Anywhere else it is user data one unhandled exception away from a log aggregator, a
    /// trace attribute or a response body.
    /// </summary>
    private const string ErrorDetail = "Include Error Detail=true";

    [Theory]
    [MemberData(nameof(HostsWithEndpoints))]
    public void EveryHostWithEndpoints_Should_TerminateExceptionsInProblemDetails(string host)
    {
        // Arrange
        string program = Program(host);

        // Assert — both halves, and each fails quietly on its own. Without UseExceptionHandler an
        // unhandled exception outside Development is a bare 500 with an empty body (and inside
        // Development, the developer exception page — a full stack trace). Without
        // AddProblemDetails the handler has no writer registered and produces the empty body again.
        program.Should().Contain(
            "AddProblemDetails()",
            "{0} needs a ProblemDetails writer for its exception handler to have anything to write",
            host);

        program.Should().Contain(
            "app.UseExceptionHandler();",
            "{0} must convert an unhandled exception into a ProblemDetails body rather than letting " +
            "the default page render it",
            host);
    }

    [Theory]
    [MemberData(nameof(HostsWithEndpoints))]
    public void NoHost_Should_MapTheDeveloperExceptionPage(string host)
    {
        // Arrange — nothing calls it today. It is one line to add, it renders the exception message,
        // the stack trace, the full set of request headers (bearer token included) and the resolved
        // configuration, and inside an `if (IsDevelopment())` it reads as harmless right up until
        // ASPNETCORE_ENVIRONMENT is set wrong on a deployment.
        string program = Program(host);

        // Assert
        program.Should().NotContain(
            "UseDeveloperExceptionPage",
            "{0} must not have an error page whose safety depends on one environment variable", host);
    }

    [Theory]
    [MemberData(nameof(NonDevelopmentSettings))]
    public void NoNonDevelopmentConfiguration_Should_AskNpgsqlForErrorDetail(string relativePath)
    {
        // Arrange
        string content = File.ReadAllText(RepositoryPaths.Backend(relativePath.Split('/')));

        // Assert
        content.Should().NotContain(
            ErrorDetail,
            "{0} is deployed configuration — Npgsql error detail carries row data into an exception " +
            "message, and from there into logs, traces and potentially a response",
            relativePath);
    }

    [Fact]
    public void DevelopmentConfiguration_Should_StillAskForErrorDetail()
    {
        // Arrange — the guard against the test above being satisfied by deleting the setting
        // everywhere. It is genuinely wanted locally: without it a constraint violation against a
        // Testcontainers database says only "23505".
        // Only the hosts that own a database: the file is enumerated one directory deep, because
        // AllDirectories also walks bin/ and would assert against build output.
        IEnumerable<string> developmentSettings = HostDirectories()
            .Select(host => RepositoryPaths.Backend("src", "API", host, "appsettings.Development.json"))
            .Where(path => File.Exists(path) &&
                           File.ReadAllText(path).Contains("\"Database\"", StringComparison.Ordinal));

        // Assert
        developmentSettings.Should().NotBeEmpty("the scan must find the Development files it excludes")
            .And.OnlyContain(path => File.ReadAllText(path).Contains(ErrorDetail, StringComparison.Ordinal));
    }

    public static TheoryData<string> HostsWithEndpoints()
    {
        var data = new TheoryData<string>();

        foreach (string host in HostDirectories().Except(HostsWithoutAnApplicationErrorSurface, StringComparer.Ordinal))
        {
            data.Add(host);
        }

        return data;
    }

    /// <summary>
    /// Every configuration file that ships to an environment: the <c>appsettings.json</c> in each
    /// host's image, the Kubernetes ConfigMap, and both compose files. <c>appsettings.*.json</c> for
    /// any non-Development environment would be caught here too, the day one is added.
    /// </summary>
    public static TheoryData<string> NonDevelopmentSettings()
    {
        var data = new TheoryData<string>();

        foreach (string host in HostDirectories())
        {
            string directory = RepositoryPaths.Backend("src", "API", host);

            foreach (string path in Directory.EnumerateFiles(directory, "appsettings*.json"))
            {
                if (Path.GetFileName(path) == "appsettings.Development.json")
                {
                    continue;
                }

                data.Add($"src/API/{host}/{Path.GetFileName(path)}");
            }
        }

        data.Add("deploy/k8s/base/config.yaml");
        data.Add("docker-compose.yml");
        data.Add("docker-compose.override.yml");

        return data;
    }

    private static string Program(string host) =>
        File.ReadAllText(RepositoryPaths.Backend("src", "API", host, "Program.cs"));

    /// <summary>
    /// Every directory under <c>src/API</c> that still holds a <c>Program.cs</c> — the filter that
    /// keeps the reverted FraudDetection host's stale <c>bin/obj</c> output out of the theory. Same
    /// as <see cref="SecurityHeaderCoverageTests"/>, for the same reason.
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
