using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Locations;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;
using FoodDeliveryService.Modules.Delivery.Domain.Shared;
using FoodDeliveryService.Modules.Delivery.Infrastructure.Database;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Locations;

/// <summary>
/// Redis-backed live location store. Two Redis structures per the plan:
/// <list type="bullet">
/// <item>a single geo set (sorted set) <c>delivery:drivers:available</c> holding only available
/// drivers, so GEOSEARCH answers "nearest available" in one round trip against a small set;</item>
/// <item>a per-driver hash <c>delivery:driver:{id}:location</c> carrying the last position with a
/// TTL — the freshness signal the geo set can't express, and the source for GetCurrent.</item>
/// </list>
/// Position history is appended to Postgres (append-only telemetry, no aggregate). A Cosmos
/// implementation of this same interface is Milestone G.
/// </summary>
internal sealed class RedisDriverLocationStore(
    IConnectionMultiplexer connectionMultiplexer,
    DeliveryDbContext dbContext,
    IOptions<DriverLocationStoreOptions> options,
    ILogger<RedisDriverLocationStore> logger) : IDriverLocationStore
{
    private const string AvailablePoolKey = "delivery:drivers:available";

    // Feature 2.2 (Real-Time). Deliberately off the bus (plan §4.1): this is the highest-traffic
    // write in the system, so it gets a fire-and-forget Redis PUBLISH alongside the existing GEO
    // write rather than a RabbitMQ message. The RealTime service subscribes and forwards positions
    // to the tracking customer; a lost frame is immaterial (best-effort, no replay).
    private const string LocationChannel = "delivery:driver-locations";

    private static readonly RedisValue LatitudeField = "lat";
    private static readonly RedisValue LongitudeField = "lon";
    private static readonly RedisValue RecordedOnUtcField = "recordedOnUtc";

    private readonly DriverLocationStoreOptions _options = options.Value;

    private IDatabase Database => connectionMultiplexer.GetDatabase();

    public async Task RecordAsync(
        Guid driverId,
        GeoCoordinate location,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);

        // The live position hash: overwritten each report and re-armed with the freshness TTL. It
        // is written for every on-duty driver (available OR busy) so the tracking screen can read a
        // busy driver's position; pool membership is a separate concern (Enter/LeaveAvailablePool).
        RedisKey locationKey = LocationKey(driverId);

        var entries = new HashEntry[]
        {
            new(LatitudeField, location.Latitude),
            new(LongitudeField, location.Longitude),
            new(RecordedOnUtcField, utcNow.ToString("O", CultureInfo.InvariantCulture))
        };

        await Database.HashSetAsync(locationKey, entries);
        await Database.KeyExpireAsync(locationKey, TimeSpan.FromSeconds(_options.LocationTtlInSeconds));

        await PublishLocationAsync(driverId, location, utcNow);

        // Durable history. No outbox involvement — nothing reacts to a position report — so this is
        // a plain insert saved on its own; the SaveChanges interceptor finds no domain events.
        dbContext.DriverLocationHistory.Add(DriverLocationHistoryEntry.Record(driverId, location, utcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<DriverLocation?> GetCurrentAsync(Guid driverId, CancellationToken cancellationToken = default)
    {
        HashEntry[] entries = await Database.HashGetAllAsync(LocationKey(driverId));

        if (entries.Length == 0)
        {
            return null;
        }

        return MapLocation(driverId, entries);
    }

    public async Task<IReadOnlyCollection<NearbyDriver>> FindNearestAvailableAsync(
        GeoCoordinate origin,
        double radiusKm,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(origin);

        using Activity? activity = DeliveryDiagnostics.ActivitySource.StartActivity("FindNearestAvailableDrivers");
        activity?.SetTag("delivery.search.radius_km", radiusKm);
        activity?.SetTag("delivery.search.limit", limit);

        GeoRadiusResult[] results = await Database.GeoSearchAsync(
            AvailablePoolKey,
            origin.Longitude,
            origin.Latitude,
            new GeoSearchCircle(radiusKm, GeoUnit.Kilometers),
            count: limit,
            order: Order.Ascending,
            options: GeoRadiusOptions.WithCoordinates | GeoRadiusOptions.WithDistance);

        var nearby = new List<NearbyDriver>(results.Length);

        foreach (GeoRadiusResult result in results)
        {
            if (result.Position is null || result.Distance is null)
            {
                continue;
            }

            var driverId = Guid.Parse((string)result.Member!);

            // Drop a driver whose freshness key has expired: the geo entry can outlive the driver
            // (no per-member TTL), so a lingering position is not a real candidate.
            if (!await Database.KeyExistsAsync(LocationKey(driverId)))
            {
                continue;
            }

            Result<GeoCoordinate> coordinate = GeoCoordinate.Create(
                result.Position.Value.Latitude,
                result.Position.Value.Longitude);

            if (coordinate.IsFailure)
            {
                continue;
            }

            nearby.Add(new NearbyDriver(driverId, coordinate.Value, result.Distance.Value));
        }

        activity?.SetTag("delivery.search.candidate_count", nearby.Count);

        return nearby;
    }

    public async Task EnterAvailablePoolAsync(Guid driverId, CancellationToken cancellationToken = default)
    {
        // Enroll at the driver's current position. No fresh position yet (e.g. they went available
        // before their first report) means nothing to search against — their next report enrolls
        // them, so this is simply a no-op rather than an error.
        HashEntry[] entries = await Database.HashGetAllAsync(LocationKey(driverId));

        if (entries.Length == 0)
        {
            return;
        }

        DriverLocation location = MapLocation(driverId, entries);

        await Database.GeoAddAsync(
            AvailablePoolKey,
            location.Location.Longitude,
            location.Location.Latitude,
            driverId.ToString());
    }

    public async Task LeaveAvailablePoolAsync(Guid driverId, CancellationToken cancellationToken = default)
    {
        // ZREM from the geo set. The position hash is left in place so an assigned/offline driver's
        // last location is still readable for tracking; it lapses on its own TTL.
        await Database.SortedSetRemoveAsync(AvailablePoolKey, driverId.ToString());
    }

    // Fire-and-forget: a dropped position frame is never a correctness problem (the RealTime
    // service has no durability guarantee here either), so a publish failure is swallowed and
    // logged rather than surfaced to the caller — this must never fault a location report.
    private async Task PublishLocationAsync(Guid driverId, GeoCoordinate location, DateTime utcNow)
    {
        try
        {
            var message = new DriverLocationPublishedMessage(driverId, location.Latitude, location.Longitude, utcNow);
            await connectionMultiplexer.GetSubscriber().PublishAsync(
                RedisChannel.Literal(LocationChannel),
                JsonSerializer.Serialize(message));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to publish driver-location update for driver {DriverId}", driverId);
        }
    }

    private static RedisKey LocationKey(Guid driverId) => $"delivery:driver:{driverId}:location";

    private static DriverLocation MapLocation(Guid driverId, HashEntry[] entries)
    {
        double latitude = 0;
        double longitude = 0;
        DateTime recordedOnUtc = default;

        foreach (HashEntry entry in entries)
        {
            if (entry.Name == LatitudeField)
            {
                latitude = (double)entry.Value;
            }
            else if (entry.Name == LongitudeField)
            {
                longitude = (double)entry.Value;
            }
            else if (entry.Name == RecordedOnUtcField)
            {
                recordedOnUtc = DateTime.Parse(
                    entry.Value!,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind);
            }
        }

        return new DriverLocation(driverId, GeoCoordinate.Create(latitude, longitude).Value, recordedOnUtc);
    }
}
