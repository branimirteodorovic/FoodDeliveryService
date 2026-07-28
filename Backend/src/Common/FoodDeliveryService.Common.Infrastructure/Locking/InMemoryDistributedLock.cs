using System.Collections.Concurrent;
using FoodDeliveryService.Common.Application.Locking;

namespace FoodDeliveryService.Common.Infrastructure.Locking;

/// <summary>
/// Process-local fallback registered only when Redis is unreachable at startup — the exact
/// counterpart of the in-memory <c>IDistributedCache</c> fallback next to it, so a developer can
/// run a service without the container stack. It honours the whole <see cref="IDistributedLock"/>
/// contract (single winner, TTL takeover, owner-checked release) but obviously only within one
/// process, so it protects nothing in a multi-replica deployment.
/// </summary>
internal sealed class InMemoryDistributedLock : IDistributedLock
{
    private readonly ConcurrentDictionary<string, LockEntry> _held = new(StringComparer.Ordinal);

    public Task<IAsyncDisposable?> TryAcquireAsync(
        string resource,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        var entry = new LockEntry(Guid.NewGuid().ToString("N"), DateTime.UtcNow.Add(ttl));

        while (true)
        {
            if (_held.TryAdd(resource, entry))
            {
                return Task.FromResult<IAsyncDisposable?>(new InMemoryLockHandle(_held, resource, entry));
            }

            if (!_held.TryGetValue(resource, out LockEntry? current))
            {
                // Released between the failed add and this read — go round again and take it.
                continue;
            }

            if (current.ExpiresOnUtc > DateTime.UtcNow)
            {
                return Task.FromResult<IAsyncDisposable?>(null);
            }

            // The holder's TTL lapsed (they crashed, or ran long): take over, but only if nobody
            // beat us to the same conclusion — TryUpdate's comparison is the compare-and-swap.
            if (_held.TryUpdate(resource, entry, current))
            {
                return Task.FromResult<IAsyncDisposable?>(new InMemoryLockHandle(_held, resource, entry));
            }
        }
    }

    private sealed record LockEntry(string Token, DateTime ExpiresOnUtc);

    private sealed class InMemoryLockHandle(
        ConcurrentDictionary<string, LockEntry> held,
        string resource,
        LockEntry entry) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            // Value-compared removal — the same owner check the Redis implementation does in Lua:
            // a handle whose TTL already lapsed must not evict whoever took the resource over.
            held.TryRemove(new KeyValuePair<string, LockEntry>(resource, entry));

            return ValueTask.CompletedTask;
        }
    }
}
