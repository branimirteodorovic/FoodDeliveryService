using AwesomeAssertions;
using FoodDeliveryService.Common.Presentation.Health;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FoodDeliveryService.Common.UnitTests.Health;

/// <summary>
/// The probe endpoints are nothing but a tag predicate over the registered checks, so these tests
/// drive <see cref="HealthCheckService"/> with the same predicates
/// <see cref="HealthProbeEndpointExtensions.MapHealthProbes"/> maps — no HTTP needed to prove that
/// liveness is dependency-free and readiness is not. The end-to-end status codes are asserted in
/// <c>Orders.IntegrationTests/Health/HealthProbeTests</c>.
/// </summary>
public class HealthProbeTests
{
    private const string DependencyCheckName = "a-dependency";

    [Fact]
    public async Task Liveness_Should_SelectOnlyTheSelfCheck()
    {
        // Arrange
        HealthCheckService healthChecks = BuildModuleHostChecks();

        // Act
        HealthReport report = await healthChecks.CheckHealthAsync(
            registration => registration.Tags.Contains(HealthCheckTags.Live),
            TestContext.Current.CancellationToken);

        // Assert
        report.Entries.Keys.Should().Equal(HealthChecksBuilderExtensions.LivenessCheckName);
        report.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Readiness_Should_SelectOnlyTheDependencyChecks()
    {
        // Arrange
        HealthCheckService healthChecks = BuildModuleHostChecks();

        // Act
        HealthReport report = await healthChecks.CheckHealthAsync(
            registration => registration.Tags.Contains(HealthCheckTags.Ready),
            TestContext.Current.CancellationToken);

        // Assert — on a module host the self check is live-only, so it must not appear here.
        report.Entries.Keys.Should().Equal(DependencyCheckName);
    }

    [Fact]
    public async Task Aggregate_Should_SelectEveryCheck()
    {
        // Arrange
        HealthCheckService healthChecks = BuildModuleHostChecks();

        // Act — no predicate: the unchanged GET /health.
        HealthReport report = await healthChecks.CheckHealthAsync(TestContext.Current.CancellationToken);

        // Assert
        report.Entries.Should()
            .ContainKeys(HealthChecksBuilderExtensions.LivenessCheckName, DependencyCheckName)
            .And.HaveCount(2);
    }

    /// <summary>
    /// The behaviour a Kubernetes readiness probe relies on: a downed dependency pulls the pod from
    /// rotation without restarting it, because liveness never sees the dependency at all.
    /// </summary>
    [Fact]
    public async Task DownedDependency_Should_FailReadiness_WhileLivenessStaysHealthy()
    {
        // Arrange
        HealthCheckService healthChecks = BuildModuleHostChecks(dependencyStatus: HealthStatus.Unhealthy);

        // Act
        HealthReport readiness = await healthChecks.CheckHealthAsync(
            registration => registration.Tags.Contains(HealthCheckTags.Ready),
            TestContext.Current.CancellationToken);

        HealthReport liveness = await healthChecks.CheckHealthAsync(
            registration => registration.Tags.Contains(HealthCheckTags.Live),
            TestContext.Current.CancellationToken);

        // Assert
        readiness.Status.Should().Be(HealthStatus.Unhealthy);
        liveness.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task GatewayLivenessCheck_Should_AnswerBothProbes()
    {
        // Arrange — the Gateway's readiness equals its liveness: one self check, both tags.
        HealthCheckService healthChecks = BuildChecks(builder =>
            builder.AddLivenessCheck(HealthCheckTags.Ready));

        // Act
        HealthReport liveness = await healthChecks.CheckHealthAsync(
            registration => registration.Tags.Contains(HealthCheckTags.Live),
            TestContext.Current.CancellationToken);

        HealthReport readiness = await healthChecks.CheckHealthAsync(
            registration => registration.Tags.Contains(HealthCheckTags.Ready),
            TestContext.Current.CancellationToken);

        // Assert
        liveness.Entries.Keys.Should().Equal(HealthChecksBuilderExtensions.LivenessCheckName);
        readiness.Entries.Keys.Should().Equal(HealthChecksBuilderExtensions.LivenessCheckName);
        readiness.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task UntaggedCheck_Should_BeInvisibleToBothProbes()
    {
        // Arrange — the failure mode the contract warns about: a dependency check registered without
        // a tag is reported by the aggregate and by nothing else, so a real outage never reaches a
        // probe. Asserted so the rule is a test, not a comment.
        HealthCheckService healthChecks = BuildChecks(builder => builder
            .AddLivenessCheck()
            .AddCheck("untagged", () => HealthCheckResult.Unhealthy()));

        // Act
        HealthReport aggregate = await healthChecks.CheckHealthAsync(TestContext.Current.CancellationToken);

        HealthReport readiness = await healthChecks.CheckHealthAsync(
            registration => registration.Tags.Contains(HealthCheckTags.Ready),
            TestContext.Current.CancellationToken);

        // Assert
        aggregate.Status.Should().Be(HealthStatus.Unhealthy);
        readiness.Entries.Should().BeEmpty();
        readiness.Status.Should().Be(HealthStatus.Healthy);
    }

    /// <summary>
    /// The registration shape of a module host: the self check plus one tagged dependency standing in
    /// for Npgsql/Redis/RabbitMQ/Duende, whose real implementations are exercised by the integration
    /// suite against live containers.
    /// </summary>
    private static HealthCheckService BuildModuleHostChecks(
        HealthStatus dependencyStatus = HealthStatus.Healthy) =>
        BuildChecks(builder => builder
            .AddLivenessCheck()
            .AddCheck(
                DependencyCheckName,
                () => new HealthCheckResult(dependencyStatus),
                tags: [HealthCheckTags.Ready]));

    private static HealthCheckService BuildChecks(Action<IHealthChecksBuilder> configure)
    {
        var services = new ServiceCollection();

        configure(services.AddLogging().AddHealthChecks());

        return services.BuildServiceProvider().GetRequiredService<HealthCheckService>();
    }
}
