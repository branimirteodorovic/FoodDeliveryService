using FoodDeliveryService.Common.Application.Locking;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FoodDeliveryService.Common.Infrastructure.Locking;

/// <summary>
/// Single-node Redlock over the Redis instance the whole platform already shares: acquire is one
/// <c>SET resource token NX PX ttl</c>, release is an owner-checked delete. No extra dependency —
/// the alternative (RedLock.net) only buys the multi-master quorum algorithm, which needs several
/// independent Redis masters this deployment does not have.
/// <para>
/// The token is what makes release safe. Without it, a holder whose TTL lapsed mid-section would,
/// on the way out, delete a lock a *different* caller has since acquired — silently unlocking a
/// live critical section. Each handle therefore remembers the random token it wrote and deletes
/// the key only while that token is still there, comparing and deleting in one Lua script so the
/// two cannot be interleaved.
/// </para>
/// </summary>
internal sealed class RedisDistributedLock(
    IConnectionMultiplexer connectionMultiplexer,
    ILogger<RedisDistributedLock> logger) : IDistributedLock
{
    private const string ReleaseScript =
        """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        else
            return 0
        end
        """;

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string resource,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        var token = Guid.NewGuid().ToString("N");

        IDatabase database = connectionMultiplexer.GetDatabase();

        // NX: the write lands only when the key is absent, so the winner is decided by Redis in a
        // single round trip — there is no read-then-write window for a second caller to slip into.
        bool acquired = await database.StringSetAsync(
            resource,
            token,
            expiry: ttl,
            keepTtl: false,
            when: When.NotExists);

        return acquired ? new RedisLockHandle(database, resource, token, logger) : null;
    }

    private sealed class RedisLockHandle(
        IDatabase database,
        string resource,
        string token,
        ILogger logger) : IAsyncDisposable
    {
        private bool _released;

        public async ValueTask DisposeAsync()
        {
            if (_released)
            {
                return;
            }

            _released = true;

            // A failed release is not a failure of the work that just completed — the TTL is the
            // backstop, so this logs and moves on rather than faulting the caller on the way out.
            try
            {
                await database.ScriptEvaluateAsync(ReleaseScript, [resource], [token]);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to release the distributed lock on {Resource}; it will lapse on its TTL",
                    resource);
            }
        }
    }
}
