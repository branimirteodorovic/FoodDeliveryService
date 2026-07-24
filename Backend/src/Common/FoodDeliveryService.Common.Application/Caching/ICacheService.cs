namespace FoodDeliveryService.Common.Application.Caching;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cache-aside: returns the cached value on a hit; on a miss, invokes <paramref name="factory"/>,
    /// caches its result and returns it. A null/default result is never cached, so a transient
    /// factory failure (e.g. an RPC returning nothing) isn't pinned into the cache until expiration.
    /// </summary>
    async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        T? cached = await GetAsync<T>(key, cancellationToken);

        if (cached is not null)
        {
            return cached;
        }

        T value = await factory(cancellationToken);

        if (!EqualityComparer<T>.Default.Equals(value, default!))
        {
            await SetAsync(key, value, expiration, cancellationToken);
        }

        return value;
    }
}
