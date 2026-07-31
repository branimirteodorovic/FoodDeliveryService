using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using FoodDeliveryService.Common.Presentation.Health;
using FoodDeliveryService.Modules.Orders.IntegrationTests.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Orders.IntegrationTests.Health;

/// <summary>
/// Drives the probe endpoints on the real Orders host against live Postgres/Redis/RabbitMQ
/// containers and the locally running Identity service. The tag predicates themselves are unit
/// tested in <c>Common.UnitTests/Health/HealthProbeTests</c>; what is asserted here is the wiring no
/// unit test can reach — that the eight-host contract in <c>docs/health-probe-contract.md</c> is
/// what a kubelet would actually get.
/// </summary>
public class HealthProbeTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task AllThreeProbes_Should_ReturnHealthy_WhenEveryDependencyIsUp()
    {
        // Act — anonymous: no Bearer token is attached, exactly as the kubelet reaches them.
        HttpResponseMessage liveness = await HttpClient.GetAsync(
            HealthProbeEndpointExtensions.LivenessPath,
            TestContext.Current.CancellationToken);

        HttpResponseMessage readiness = await HttpClient.GetAsync(
            HealthProbeEndpointExtensions.ReadinessPath,
            TestContext.Current.CancellationToken);

        HttpResponseMessage aggregate = await HttpClient.GetAsync(
            HealthProbeEndpointExtensions.HealthPath,
            TestContext.Current.CancellationToken);

        // Assert
        liveness.StatusCode.Should().Be(HttpStatusCode.OK);
        readiness.StatusCode.Should().Be(HttpStatusCode.OK);
        aggregate.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Liveness_Should_ReportOnlyTheSelfCheck()
    {
        // Act
        UiHealthReport report = await GetReportAsync(HealthProbeEndpointExtensions.LivenessPath);

        // Assert — the whole point of the split: nothing an outage elsewhere can break is in here.
        report.Status.Should().Be("Healthy");
        report.Entries.Keys.Should().Equal(HealthChecksBuilderExtensions.LivenessCheckName);
    }

    [Fact]
    public async Task Readiness_Should_ReportEveryDependency()
    {
        // Act
        UiHealthReport report = await GetReportAsync(HealthProbeEndpointExtensions.ReadinessPath);

        // Assert — "masstransit-bus" is registered and tagged ready by MassTransit itself, not by the
        // host. It is asserted here so a MassTransit upgrade that changes that tag fails a test
        // instead of silently emptying bus connectivity out of the readiness set.
        report.Status.Should().Be("Healthy");
        report.Entries.Should().ContainKeys("npgsql", "redis", "rabbitmq", "Duende", "masstransit-bus");
        report.Entries.Should().NotContainKey(HealthChecksBuilderExtensions.LivenessCheckName);
    }

    [Fact]
    public async Task Aggregate_Should_ReportLivenessAndReadinessTogether()
    {
        // Act
        UiHealthReport report = await GetReportAsync(HealthProbeEndpointExtensions.HealthPath);

        // Assert — unchanged by the split: still every check, still the same payload.
        report.Status.Should().Be("Healthy");
        report.Entries.Should().ContainKeys(
            HealthChecksBuilderExtensions.LivenessCheckName,
            "npgsql",
            "redis",
            "rabbitmq",
            "Duende",
            "masstransit-bus");
    }

    /// <summary>
    /// The behaviour Kubernetes actually relies on, and the reason the split exists: a dependency
    /// goes down, readiness turns 503 so the pod leaves the load-balancer rotation, and liveness
    /// stays 200 so the kubelet does not restart it — restarting would not bring PostgreSQL back.
    /// <para>
    /// Runs against its own throwaway host rather than the shared one: killing a container the whole
    /// collection depends on would take every other test with it. The registrations mirror a module
    /// host's (<see cref="HealthChecksBuilderExtensions.AddLivenessCheck"/> plus a real
    /// <c>AddNpgSql</c> check tagged ready) with the connection pointed at a port nothing listens on.
    /// </para>
    /// </summary>
    [Fact]
    public async Task DownedDependency_Should_Fail_Readiness_WhileLivenessStaysHealthy()
    {
        // Arrange
        const string deadDatabase = "Host=localhost;Port=1;Database=nothing;Username=none;Password=none";

        var builder = WebApplication.CreateSlimBuilder();

        builder.WebHost.UseTestServer();
        builder.Services.AddHealthChecks()
            .AddLivenessCheck()
            .AddNpgSql(deadDatabase, tags: [HealthCheckTags.Ready]);

        await using WebApplication app = builder.Build();

        app.MapHealthProbes();

        await app.StartAsync(TestContext.Current.CancellationToken);

        using HttpClient client = app.GetTestClient();

        // Act
        HttpResponseMessage readiness = await client.GetAsync(
            HealthProbeEndpointExtensions.ReadinessPath,
            TestContext.Current.CancellationToken);

        HttpResponseMessage liveness = await client.GetAsync(
            HealthProbeEndpointExtensions.LivenessPath,
            TestContext.Current.CancellationToken);

        // Assert
        readiness.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        liveness.StatusCode.Should().Be(HttpStatusCode.OK);

        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    private async Task<UiHealthReport> GetReportAsync(string path)
    {
        HttpResponseMessage response = await HttpClient.GetAsync(path, TestContext.Current.CancellationToken);

        return (await response.Content.ReadFromJsonAsync<UiHealthReport>(
            JsonOptions,
            TestContext.Current.CancellationToken))!;
    }

    /// <summary>
    /// The HealthChecks.UI payload every probe renders, narrowed to what the contract promises: a
    /// status and one entry per selected check. Probes key on the status code, not on this body.
    /// </summary>
    private sealed record UiHealthReport(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("entries")] Dictionary<string, UiHealthEntry> Entries);

    private sealed record UiHealthEntry([property: JsonPropertyName("status")] string Status);
}
