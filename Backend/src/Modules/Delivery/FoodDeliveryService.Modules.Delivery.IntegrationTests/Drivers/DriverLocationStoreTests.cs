using AwesomeAssertions;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Locations;
using FoodDeliveryService.Modules.Delivery.Domain.Shared;
using FoodDeliveryService.Modules.Delivery.IntegrationTests.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace FoodDeliveryService.Modules.Delivery.IntegrationTests.Drivers;

/// <summary>
/// The geospatial path exercised directly against the real Redis container: the "nearest available"
/// search ordering and radius, pool entry/exit, and the freshness filter that drops a driver whose
/// position key has lapsed even though the geo entry lingers. Driven through <see cref="IDriverLocationStore"/>
/// rather than HTTP because that is the surface the assignment routine (Milestone E) depends on.
/// </summary>
public class DriverLocationStoreTests : BaseIntegrationTest
{
    // Belgrade centre and three offsets: two inside a 5 km radius, one (Novi Sad, ~70 km) outside.
    private static readonly GeoCoordinate Origin = GeoCoordinate.Create(44.8176, 20.4633).Value;
    private static readonly GeoCoordinate Near = GeoCoordinate.Create(44.8180, 20.4640).Value;
    private static readonly GeoCoordinate Mid = GeoCoordinate.Create(44.8300, 20.4800).Value;
    private static readonly GeoCoordinate Far = GeoCoordinate.Create(45.2671, 19.8335).Value;

    public DriverLocationStoreTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task RecordAsync_ThenGetCurrentAsync_Should_RoundTripThePosition()
    {
        await WithStoreAsync(async store =>
        {
            var driverId = Guid.NewGuid();

            await store.RecordAsync(driverId, Near, DateTime.UtcNow, TestContext.Current.CancellationToken);

            DriverLocation? current = await store.GetCurrentAsync(driverId, TestContext.Current.CancellationToken);

            current.Should().NotBeNull();
            current!.DriverId.Should().Be(driverId);
            current.Location.Latitude.Should().BeApproximately(Near.Latitude, 0.0001);
            current.Location.Longitude.Should().BeApproximately(Near.Longitude, 0.0001);
        });
    }

    [Fact]
    public async Task GetCurrentAsync_Should_ReturnNull_WhenNoPositionReported()
    {
        await WithStoreAsync(async store =>
        {
            DriverLocation? current = await store.GetCurrentAsync(
                Guid.NewGuid(),
                TestContext.Current.CancellationToken);

            current.Should().BeNull();
        });
    }

    [Fact]
    public async Task FindNearestAvailableAsync_Should_ReturnCandidatesInDistanceOrder_ExcludingOutsideRadius()
    {
        await WithStoreAsync(async store =>
        {
            var near = Guid.NewGuid();
            var mid = Guid.NewGuid();
            var far = Guid.NewGuid();

            await EnterPoolAsync(store, near, Near);
            await EnterPoolAsync(store, mid, Mid);
            await EnterPoolAsync(store, far, Far);

            IReadOnlyCollection<NearbyDriver> candidates = await store.FindNearestAvailableAsync(
                Origin,
                radiusKm: 5,
                limit: 50,
                TestContext.Current.CancellationToken);

            // Filter to this test's drivers — the pool is shared across the collection.
            var mine = candidates
                .Select(c => c.DriverId)
                .Where(id => id == near || id == mid || id == far)
                .ToList();

            mine.Should().Equal(near, mid);
            candidates.Select(c => c.DriverId).Should().NotContain(far);
        });
    }

    [Fact]
    public async Task FindNearestAvailableAsync_Should_ExcludeADriverWhoLeftThePool()
    {
        await WithStoreAsync(async store =>
        {
            var driverId = Guid.NewGuid();
            await EnterPoolAsync(store, driverId, Near);

            // The driver goes offline / is reserved.
            await store.LeaveAvailablePoolAsync(driverId, TestContext.Current.CancellationToken);

            IReadOnlyCollection<NearbyDriver> candidates = await store.FindNearestAvailableAsync(
                Origin,
                radiusKm: 5,
                limit: 50,
                TestContext.Current.CancellationToken);

            candidates.Select(c => c.DriverId).Should().NotContain(driverId);
        });
    }

    [Fact]
    public async Task FindNearestAvailableAsync_Should_ExcludeADriverWhosePositionHasGoneStale()
    {
        await WithStoreAsync(async (store, scope) =>
        {
            var driverId = Guid.NewGuid();
            await EnterPoolAsync(store, driverId, Near);

            // Simulate a crashed driver: their freshness key is gone but the geo entry lingers
            // (Redis GEO has no per-member TTL). Delete the location hash directly, leaving the
            // driver in the available sorted set.
            var multiplexer = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
            IDatabase database = multiplexer.GetDatabase();

            await database.KeyDeleteAsync($"delivery:driver:{driverId}:location");

            // Sanity: the geo member is still present, so exclusion is due to staleness, not removal.
            double? score = await database.SortedSetScoreAsync("delivery:drivers:available", driverId.ToString());
            score.Should().NotBeNull();

            IReadOnlyCollection<NearbyDriver> candidates = await store.FindNearestAvailableAsync(
                Origin,
                radiusKm: 5,
                limit: 50,
                TestContext.Current.CancellationToken);

            candidates.Select(c => c.DriverId).Should().NotContain(driverId);
        });
    }

    private static async Task EnterPoolAsync(IDriverLocationStore store, Guid driverId, GeoCoordinate location)
    {
        // Mirrors the location handler: a report writes the position, then the available driver is
        // enrolled in the geo pool at that position.
        await store.RecordAsync(driverId, location, DateTime.UtcNow, TestContext.Current.CancellationToken);
        await store.EnterAvailablePoolAsync(driverId, TestContext.Current.CancellationToken);
    }

    private async Task WithStoreAsync(Func<IDriverLocationStore, Task> action)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<IDriverLocationStore>());
    }

    private async Task WithStoreAsync(Func<IDriverLocationStore, AsyncServiceScope, Task> action)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<IDriverLocationStore>(), scope);
    }
}
