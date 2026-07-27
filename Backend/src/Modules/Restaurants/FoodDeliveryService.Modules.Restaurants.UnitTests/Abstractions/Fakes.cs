using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Restaurants.UnitTests.Abstractions;

/// <summary>
/// Records every eviction so a test can assert exactly which cache keys a command handler removed.
/// Reads always miss — these tests are about the write path, not cache-aside behavior (that is
/// covered by Common.UnitTests' <c>QueryCachingBehaviorTests</c>).
/// </summary>
internal sealed class RecordingCacheService : ICacheService
{
    public List<string> RemovedKeys { get; } = [];

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult<T?>(default);

    public Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        RemovedKeys.Add(key);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Serves a single in-memory aggregate; any other id resolves to null so the "restaurant not found"
/// failure path can be exercised without a database.
/// </summary>
internal sealed class FakeRestaurantsRepository(Restaurant? seed) : IRestaurantsRepository
{
    public Task<Restaurant?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(seed?.Id == id ? seed : null);

    public void Insert(Restaurant restaurant)
    {
        // Not needed by the menu command handlers under test — they always load an existing root.
    }
}

internal sealed class FakeRestaurantsContext(Guid userId) : IRestaurantsContext
{
    public Guid UserId { get; } = userId;

    public bool HasPermission(string permissionCode) => false;
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCallCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;

        return Task.FromResult(0);
    }
}
