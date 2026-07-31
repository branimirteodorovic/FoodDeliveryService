using System.Diagnostics.Metrics;
using FoodDeliveryService.Common.Application.Caching;

namespace FoodDeliveryService.Common.Infrastructure.Caching;

/// <summary>
/// Cache hit/miss counters. They are recorded from <see cref="CacheService"/>.<c>GetAsync</c> — the
/// single choke point every cache read passes through: the query-caching behavior's cached queries,
/// <c>GetOrCreateAsync</c> (and therefore the permission cache, the oldest and busiest consumer),
/// and any direct <c>GetAsync</c> call. Instrumenting the pipeline behavior as well would
/// double-count every cached query, so the behavior stays uninstrumented.
/// <para>
/// <c>AddInfrastructure</c> registers <see cref="MeterName"/> through <c>AddMeter</c> on the metrics
/// reader it stands up (Telemetry Feature 2.4, Milestone A), so these measurements leave the process
/// over OTLP like every other metric. A backend to point them at — Collector → Prometheus → Grafana,
/// with the hit-rate panel built from exactly these two counters — is Telemetry Milestone E.
/// </para>
/// </summary>
public static class CacheDiagnostics
{
    /// <summary>
    /// The meter Telemetry 2.4 must pass to <c>AddMeter</c> for these counters to be exported.
    /// </summary>
    public const string MeterName = "FoodDeliveryService.Cache";

    /// <summary>
    /// Tags every measurement with the cached surface (<c>restaurants:menu</c>,
    /// <c>user_permissions</c>, …) rather than the full key, keeping the time-series cardinality
    /// bounded by the number of cached query types instead of the number of rows.
    /// </summary>
    private const string KeyPrefixTagName = "cache.key_prefix";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Hits = Meter.CreateCounter<long>(
        "cache.hits",
        unit: "{lookup}",
        description: "Cache lookups served from the distributed cache.");

    private static readonly Counter<long> Misses = Meter.CreateCounter<long>(
        "cache.misses",
        unit: "{lookup}",
        description: "Cache lookups that found no entry and fell through to the source.");

    public static void RecordHit(string key) =>
        Hits.Add(1, new KeyValuePair<string, object?>(KeyPrefixTagName, CacheKeys.Prefix(key)));

    public static void RecordMiss(string key) =>
        Misses.Add(1, new KeyValuePair<string, object?>(KeyPrefixTagName, CacheKeys.Prefix(key)));
}
