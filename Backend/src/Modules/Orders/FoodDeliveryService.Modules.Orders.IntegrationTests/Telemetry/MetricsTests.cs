using AwesomeAssertions;
using FoodDeliveryService.Common.Infrastructure.Caching;
using FoodDeliveryService.Modules.Orders.IntegrationTests.Abstractions;
using OpenTelemetry.Metrics;

namespace FoodDeliveryService.Modules.Orders.IntegrationTests.Telemetry;

/// <summary>
/// The metrics pillar on the real Orders host. Emission is cheap to get right and easy to unit
/// test; what could not be tested until the pipeline existed is <b>collection</b> — that the meter
/// provider <c>AddInfrastructure</c> stands up is actually listening to the meters the platform
/// records into. Every assertion here therefore goes through the host's own <c>MeterProvider</c>
/// (<see cref="IntegrationTestWebAppFactory.CollectMetrics"/>), not through a listener attached to
/// the meter directly, which would pass even with the whole pillar removed.
/// </summary>
public class MetricsTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task DrivingAnEndpoint_Should_Record_TheHttpServerDurationHistogram()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();

        // Act
        HttpResponseMessage response = await client.GetAsync("orders", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        IReadOnlyList<Metric> metrics = Factory.CollectMetrics();

        // Assert — the transport-level RED signal, free from AddAspNetCoreInstrumentation. Its
        // application-boundary counterpart (per command/query, with the outcome derived from the
        // Result) is Milestone B's RequestMetricsBehavior.
        Metric duration = metrics.Should()
            .ContainSingle(metric => metric.Name == "http.server.request.duration").Subject;

        duration.MeterName.Should().Be("Microsoft.AspNetCore.Hosting");
        CountMeasurements(duration).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DrivingAnEndpoint_Should_Record_TheCacheHitOrMissCounters()
    {
        // Arrange — an authenticated request resolves permissions through the Redis-cached
        // IPermissionService, so it always performs a cache lookup.
        HttpClient client = await GetAuthenticatedHttpClientAsync();

        // Act
        HttpResponseMessage response = await client.GetAsync("orders", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        IReadOnlyList<Metric> metrics = Factory.CollectMetrics();

        // Assert — CacheService has recorded these since Caching 2.3 Milestone E, and until this
        // milestone registered CacheDiagnostics.MeterName nothing collected a single one of them.
        // Which of the two fires depends on whether this test ran after another one warmed the
        // permission key, so assert on the pair.
        metrics
            .Where(metric => metric.MeterName == CacheDiagnostics.MeterName)
            .Select(metric => metric.Name)
            .Should().IntersectWith(["cache.hits", "cache.misses"]);
    }

    [Fact]
    public void ModuleCounter_Should_BeCollected_WhenRegisteredWithAddModuleDiagnostics()
    {
        // Act
        SmokeDiagnostics.Operations.Add(1, new KeyValuePair<string, object?>("outcome", "ok"));

        IReadOnlyList<Metric> metrics = Factory.CollectMetrics();

        // Assert — the shape every module counter added from Milestone B onwards will have.
        Metric smoke = metrics.Should()
            .ContainSingle(metric => metric.MeterName == SmokeDiagnostics.Name).Subject;

        smoke.Name.Should().Be("smoke.operations");
        smoke.Unit.Should().Be("{operation}");
    }

    private static int CountMeasurements(Metric metric)
    {
        int count = 0;

        foreach (ref readonly MetricPoint point in metric.GetMetricPoints())
        {
            count += (int)point.GetHistogramCount();
        }

        return count;
    }
}
