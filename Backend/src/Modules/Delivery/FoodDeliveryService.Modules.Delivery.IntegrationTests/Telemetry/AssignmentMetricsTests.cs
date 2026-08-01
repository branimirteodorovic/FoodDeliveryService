using AwesomeAssertions;
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;
using FoodDeliveryService.Modules.Delivery.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using DeliveryAggregate = FoodDeliveryService.Modules.Delivery.Domain.Deliveries.Delivery;

namespace FoodDeliveryService.Modules.Delivery.IntegrationTests.Telemetry;

/// <summary>
/// The assignment business metrics on the real Delivery host. What matters here is <b>collection</b>
/// as much as emission: the instruments are created on <c>DeliveryDiagnostics.Meter</c>, and the only
/// thing making them visible is the one <c>AddModuleDiagnostics(DeliveryDiagnostics.Name)</c> line in
/// the host — delete it and every assertion below fails while the production code keeps happily
/// recording into nothing. So the assertions go through the host's own <c>MeterProvider</c>.
/// </summary>
public class AssignmentMetricsTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task AssignmentWithNoDriverInRadius_Should_Record_TheNoDriverOutcome()
    {
        // Arrange — a restaurant in the middle of the Atlantic, far from every other test's city, so
        // the 5 km search radius guarantees an empty candidate set.
        var orderId = Guid.NewGuid();

        var eventBus = Factory.Services.GetRequiredService<IEventBus>();

        // Act — the same trigger production uses: Orders says the food is ready, the inbox drives
        // the create-and-offer routine, and the routine finds nobody.
        await eventBus.PublishAsync(
            new OrderReadyForPickupIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                orderId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                -30.5,
                -20.5,
                Faker.Address.StreetAddress(),
                Faker.Address.City(),
                Faker.Address.ZipCode(),
                Faker.Address.Country(),
                deliveryNotes: null,
                -30.51,
                -20.51,
                18.00m,
                DateTime.UtcNow.AddMinutes(-20)),
            TestContext.Current.CancellationToken);

        Result<DeliveryAggregate> unassigned = await Poller.WaitAsync(TimeSpan.FromSeconds(30), async () =>
        {
            await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IDeliveriesRepository>();

            DeliveryAggregate? delivery =
                await repository.GetByOrderIdAsync(orderId, TestContext.Current.CancellationToken);

            return delivery is not null && delivery.Status == DeliveryStatus.Unassigned
                ? Result.Success(delivery)
                : Result.Failure<DeliveryAggregate>(
                    Error.NotFound("Delivery.NotParked", "The delivery has not been parked Unassigned yet"));
        });

        unassigned.IsSuccess.Should().BeTrue("no candidates in radius must park the delivery");

        IReadOnlyList<Metric> metrics = Factory.CollectMetrics();

        // Assert — `no_driver` is the outcome that waits on a human, so it is the one the assignment
        // panel alerts on. It is deliberately NOT folded in with lock contention or a guard failure.
        Metric outcomes = metrics.Should()
            .ContainSingle(metric => metric.Name == "delivery.assignment.outcome").Subject;

        outcomes.MeterName.Should().Be("FoodDeliveryService.Delivery");
        SumForOutcome(outcomes, "no_driver").Should().BeGreaterThan(0);

        // The routine's duration is recorded for every turn, tagged with the same outcome, so a
        // dashboard can separate "spent 200 ms searching and found nobody" from "returned instantly
        // because another trigger held the lock".
        Metric duration = metrics.Should()
            .ContainSingle(metric => metric.Name == "delivery.assignment.duration").Subject;

        CountForOutcome(duration, "no_driver").Should().BeGreaterThan(0);
    }

    private static long SumForOutcome(Metric metric, string outcome)
    {
        long sum = 0;

        foreach (ref readonly MetricPoint point in metric.GetMetricPoints())
        {
            if (HasOutcome(in point, outcome))
            {
                sum += point.GetSumLong();
            }
        }

        return sum;
    }

    private static long CountForOutcome(Metric metric, string outcome)
    {
        long count = 0;

        foreach (ref readonly MetricPoint point in metric.GetMetricPoints())
        {
            if (HasOutcome(in point, outcome))
            {
                count += point.GetHistogramCount();
            }
        }

        return count;
    }

    /// <summary>
    /// The readers are cumulative, so one point exists per outcome seen since the host started —
    /// every other assignment this collection drove is in there too. Selecting the point by its
    /// outcome tag is what keeps the assertion about this test.
    /// </summary>
    private static bool HasOutcome(ref readonly MetricPoint point, string outcome)
    {
        foreach (KeyValuePair<string, object?> tag in point.Tags)
        {
            if (tag.Key == "outcome" && string.Equals(tag.Value as string, outcome, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
