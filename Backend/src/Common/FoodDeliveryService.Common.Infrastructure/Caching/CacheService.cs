using System.Buffers;
using System.Text.Json;
using FoodDeliveryService.Common.Application.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodDeliveryService.Common.Infrastructure.Caching;

internal sealed class CacheService(
    IDistributedCache cache,
    IOptions<CachingSettings> cachingSettings,
    ILogger<CacheService> logger) : ICacheService
{
    private readonly CachingSettings _settings = cachingSettings.Value;

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        byte[]? bytes = await cache.GetAsync(key, cancellationToken);

        // Every cache read in the platform funnels through here, so this is where hit rate is
        // measured (see CacheDiagnostics). The log carries the full key — logs have no cardinality
        // budget to blow — while the counter tag is only its prefix.
        if (bytes is null)
        {
            CacheDiagnostics.RecordMiss(key);
            logger.LogDebug("Cache miss for {CacheKey}", key);

            return default;
        }

        CacheDiagnostics.RecordHit(key);
        logger.LogDebug("Cache hit for {CacheKey}", key);

        return Deserialize<T>(bytes);
    }

    public Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        byte[] bytes = Serialize(value);

        return cache.SetAsync(key, bytes, CacheOptions.Create(expiration, _settings), cancellationToken);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(key, cancellationToken);

    private static T Deserialize<T>(byte[] bytes)
    {
        return JsonSerializer.Deserialize<T>(bytes)!;
    }

    private static byte[] Serialize<T>(T value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        JsonSerializer.Serialize(writer, value);
        return buffer.WrittenSpan.ToArray();
    }
}
