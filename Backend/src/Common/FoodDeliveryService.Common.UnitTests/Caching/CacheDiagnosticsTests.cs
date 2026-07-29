using System.Diagnostics.Metrics;
using AwesomeAssertions;
using FoodDeliveryService.Common.Application.Behaviors;
using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Infrastructure.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Common.UnitTests.Caching;

/// <summary>
/// The counters are process-wide statics and xUnit runs test classes in parallel, so every test
/// here works against its own unique key prefix and filters recorded measurements by it. That is
/// what keeps these assertions immune to cache lookups happening in other suites at the same time.
/// </summary>
public class CacheDiagnosticsTests
{
    private const string HitsInstrument = "cache.hits";
    private const string MissesInstrument = "cache.misses";

    private readonly ICacheService _cacheService = new ServiceCollection()
        .AddLogging()
        .AddDistributedMemoryCache()
        .AddSingleton<ICacheService, CacheService>()
        .BuildServiceProvider()
        .GetRequiredService<ICacheService>();

    [Fact]
    public async Task GetAsync_Should_RecordMiss_WhenKeyIsCold()
    {
        // Arrange
        string area = UniqueArea();
        string key = CacheKeys.Create(area, Guid.NewGuid());

        using var recorder = new CacheMeasurementRecorder(area);

        // Act
        string? value = await _cacheService.GetAsync<string>(key, TestContext.Current.CancellationToken);

        // Assert
        value.Should().BeNull();
        recorder.Total(MissesInstrument).Should().Be(1);
        recorder.Total(HitsInstrument).Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_Should_RecordHit_WhenKeyIsWarm()
    {
        // Arrange
        string area = UniqueArea();
        string key = CacheKeys.Create(area, Guid.NewGuid());

        await _cacheService.SetAsync(
            key,
            "value",
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);

        using var recorder = new CacheMeasurementRecorder(area);

        // Act
        string? value = await _cacheService.GetAsync<string>(key, TestContext.Current.CancellationToken);

        // Assert
        value.Should().Be("value");
        recorder.Total(HitsInstrument).Should().Be(1);
        recorder.Total(MissesInstrument).Should().Be(0);
    }

    [Fact]
    public async Task GetOrCreateAsync_Should_RecordMissThenHit_AcrossTwoCalls()
    {
        // Arrange
        string area = UniqueArea();
        string key = CacheKeys.Create(area, Guid.NewGuid());

        using var recorder = new CacheMeasurementRecorder(area);

        // Act — the first call populates the cache, the second is served from it.
        await _cacheService.GetOrCreateAsync(
            key,
            _ => Task.FromResult("value"),
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);

        await _cacheService.GetOrCreateAsync(
            key,
            _ => Task.FromResult("value"),
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);

        // Assert — this is the permission-cache path, which the pipeline behavior never sees.
        recorder.Total(MissesInstrument).Should().Be(1);
        recorder.Total(HitsInstrument).Should().Be(1);
    }

    [Fact]
    public async Task QueryCachingBehavior_Should_RecordMissThenHit_ForTheSameQuery()
    {
        // Arrange
        string area = UniqueArea();
        var behavior = new QueryCachingBehavior<CachedTestQuery, Result<string>>(_cacheService);
        var request = new CachedTestQuery(CacheKeys.Create(area, Guid.NewGuid()));

        using var recorder = new CacheMeasurementRecorder(area);

        // Act
        await behavior.Handle(
            request,
            _ => Task.FromResult(Result.Success("value")),
            TestContext.Current.CancellationToken);

        await behavior.Handle(
            request,
            _ => Task.FromResult(Result.Success("value")),
            TestContext.Current.CancellationToken);

        // Assert — one lookup per Handle, counted once each; the behavior itself emits nothing, so
        // there is no double-counting on top of CacheService.
        recorder.Total(MissesInstrument).Should().Be(1);
        recorder.Total(HitsInstrument).Should().Be(1);
    }

    [Fact]
    public async Task RecordedTag_Should_CarryKeyPrefixOnly_WithoutTheId()
    {
        // Arrange
        string area = UniqueArea();
        var id = Guid.NewGuid();
        string key = CacheKeys.Create(area, "menu", id);

        using var recorder = new CacheMeasurementRecorder($"{area}:menu");

        // Act
        await _cacheService.GetAsync<string>(key, TestContext.Current.CancellationToken);

        // Assert — cardinality is bounded by the cached surface, not by the number of rows.
        recorder.Total(MissesInstrument).Should().Be(1);
        recorder.RecordedPrefixes.Should().AllBe($"{area}:menu");
        recorder.RecordedPrefixes.Should().NotContain(prefix => prefix!.Contains(id.ToString(), StringComparison.Ordinal));
    }

    private static string UniqueArea() => $"tests-{Guid.NewGuid():N}";

    private sealed record CachedTestQuery(string CacheKey) : ICachedQuery
    {
        public TimeSpan? Expiration => TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// Listens to the cache meter through the BCL <see cref="MeterListener"/> rather than
    /// <c>MetricCollector&lt;T&gt;</c> from <c>Microsoft.Extensions.Diagnostics.Testing</c> —
    /// Telemetry 2.4 already plans to bring that package in, and counter emission alone doesn't
    /// justify pulling it forward.
    /// </summary>
    private sealed class CacheMeasurementRecorder : IDisposable
    {
        private const string KeyPrefixTagName = "cache.key_prefix";

        private readonly MeterListener _listener = new();
        private readonly List<(string Instrument, long Value, string? KeyPrefix)> _measurements = [];
        private readonly Lock _gate = new();
        private readonly string _keyPrefix;

        public CacheMeasurementRecorder(string keyPrefix)
        {
            _keyPrefix = keyPrefix;

            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == CacheDiagnostics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>(OnMeasurementRecorded);
            _listener.Start();
        }

        public IReadOnlyList<string?> RecordedPrefixes
        {
            get
            {
                lock (_gate)
                {
                    return [.. _measurements.Select(measurement => measurement.KeyPrefix)];
                }
            }
        }

        public long Total(string instrument)
        {
            lock (_gate)
            {
                return _measurements
                    .Where(measurement => measurement.Instrument == instrument)
                    .Sum(measurement => measurement.Value);
            }
        }

        public void Dispose() => _listener.Dispose();

        private void OnMeasurementRecorded(
            Instrument instrument,
            long measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
        {
            string? keyPrefix = null;

            foreach (KeyValuePair<string, object?> tag in tags)
            {
                if (tag.Key == KeyPrefixTagName)
                {
                    keyPrefix = tag.Value as string;
                }
            }

            // Other test classes run in parallel against the same process-wide counters; anything
            // outside this test's own key prefix is somebody else's measurement.
            if (keyPrefix != _keyPrefix)
            {
                return;
            }

            lock (_gate)
            {
                _measurements.Add((instrument.Name, measurement, keyPrefix));
            }
        }
    }
}
