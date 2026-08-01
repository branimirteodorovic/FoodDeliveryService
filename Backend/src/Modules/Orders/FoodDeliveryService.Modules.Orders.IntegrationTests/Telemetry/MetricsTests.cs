using AwesomeAssertions;
using FoodDeliveryService.Common.Application.Diagnostics;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Infrastructure.Caching;
using FoodDeliveryService.Modules.Orders.Application.Diagnostics;
using FoodDeliveryService.Modules.Orders.Application.Orders.GetOrders;
using FoodDeliveryService.Modules.Orders.Domain.Orders;
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
    public async Task DrivingAnEndpoint_Should_Record_TheApplicationRedSignal()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();

        // Act
        HttpResponseMessage response = await client.GetAsync("orders", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        IReadOnlyList<Metric> metrics = Factory.CollectMetrics();

        // Assert — the application-boundary half of RED, recorded by RequestMetricsBehavior and
        // keyed by the request type rather than by route. The transport signal above cannot tell a
        // handler that answered Result.Failure from one that succeeded; this one can.
        Metric duration = metrics.Should()
            .ContainSingle(metric =>
                metric.Name == "app.request.duration" &&
                metric.MeterName == ApplicationDiagnostics.Name).Subject;

        HasTag(duration, "request", nameof(GetOrdersQuery)).Should().BeTrue();
        HasTag(duration, "outcome", "success").Should().BeTrue();

        metrics.Should().Contain(metric => metric.Name == "app.requests");
    }

    [Fact]
    public async Task PlacingAnOrder_Should_Record_TheBusinessCounters()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();

        // Act — placement raises OrderPlacedDomainEvent, which the outbox job dispatches to the
        // handler that records the counters. So wait for the outbox, not for the HTTP response.
        await PlaceOrderAsync(client);

        Result<bool> processed = await WaitForProcessedOutboxEventAsync(nameof(OrderPlacedDomainEvent));
        processed.IsSuccess.Should().BeTrue("the OrderPlaced domain event must be dispatched by the outbox");

        IReadOnlyList<Metric> metrics = Factory.CollectMetrics();

        // Assert — orders/min for the business dashboard, plus the entry edge of the transition
        // graph. Both come from the Orders meter, which exists only because Orders.Api makes the one
        // AddModuleDiagnostics(OrdersDiagnostics.Name) call.
        Metric placed = metrics.Should()
            .ContainSingle(metric =>
                metric.Name == "orders.placed" &&
                metric.MeterName == OrdersDiagnostics.Name).Subject;

        SumLong(placed).Should().BeGreaterThan(0);

        Metric transitions = metrics.Should()
            .ContainSingle(metric => metric.Name == "orders.state_transition").Subject;

        HasTag(transitions, "from", "none").Should().BeTrue("placement starts the lifecycle");
        HasTag(transitions, "to", nameof(OrderStatus.Pending)).Should().BeTrue();
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

    private static long SumLong(Metric metric)
    {
        long sum = 0;

        foreach (ref readonly MetricPoint point in metric.GetMetricPoints())
        {
            sum += point.GetSumLong();
        }

        return sum;
    }

    /// <summary>
    /// Whether ANY exported point of the metric carries the tag. The readers are cumulative, so a
    /// metric exported here carries one point per tag combination seen since the host started —
    /// including combinations other tests in this collection produced. Asserting that the expected
    /// combination is present is the honest assertion; asserting it is the only one is not.
    /// </summary>
    private static bool HasTag(Metric metric, string key, string value)
    {
        foreach (ref readonly MetricPoint point in metric.GetMetricPoints())
        {
            foreach (KeyValuePair<string, object?> tag in point.Tags)
            {
                if (tag.Key == key && string.Equals(tag.Value as string, value, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
