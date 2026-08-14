using System.Diagnostics;
using FoodDeliveryService.Common.Presentation.RateLimiting;
using StackExchange.Redis;

namespace FoodDeliveryService.Gateway.RateLimiting;

/// <summary>
/// The shared fixed-window counter behind the edge limiter — one Redis round trip per request.
/// <para>
/// <b>This is the part that makes the limit real under replicas.</b> The Gateway is the one service
/// that can scale freely (no database, no broker, no sticky sessions), and
/// <c>KUBERNETES_PHASE2_PLAN.md</c> §5.4 records the catch: it could only scale freely *because* no
/// limiter existed. Per-pod in-process buckets would multiply the configured limit by the replica
/// count silently. A counter in the Redis every service already shares does not.
/// </para>
/// <para>
/// The whole decision is one Lua script so <c>INCR</c>, the first-write <c>PEXPIRE</c> and the
/// <c>PTTL</c> read cannot be interleaved with another Gateway pod's. Without the script, two
/// requests racing on a brand-new key can both see <c>current == 1</c> and both set the expiry, or a
/// key can be incremented by a pod that crashes before setting one — leaving a counter that never
/// resets and a client throttled forever.
/// </para>
/// </summary>
internal sealed class RedisRateLimitStore(
    IConnectionMultiplexer connectionMultiplexer,
    ILogger<RedisRateLimitStore> logger) : IRateLimitStore
{
    /// <summary>
    /// <c>{admitted, retryAfterMs}</c>. The <c>ttl &lt; 0</c> guard covers the one case that would
    /// otherwise fail open: a key that exists without an expiry because a pod died between the
    /// <c>INCR</c> and the <c>PEXPIRE</c> of some earlier request. Repairing it here means the window
    /// heals on the next request rather than throttling a client until someone notices.
    /// </summary>
    private const string AcquireScript =
        """
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then
            redis.call('PEXPIRE', KEYS[1], ARGV[2])
        end
        if current > tonumber(ARGV[1]) then
            local ttl = redis.call('PTTL', KEYS[1])
            if ttl < 0 then
                redis.call('PEXPIRE', KEYS[1], ARGV[2])
                ttl = tonumber(ARGV[2])
            end
            return {0, ttl}
        end
        return {1, 0}
        """;

    /// <summary>
    /// Minimum gap between "Redis is unreachable" warnings.
    /// <para>
    /// A Redis outage means <i>every</i> request through the single public entry point takes this
    /// path. Logging each one would turn a degradation into an incident of its own — Seq saturated,
    /// disk filling, and the real cause buried under its own alarm.
    /// </para>
    /// </summary>
    private static readonly TimeSpan WarningInterval = TimeSpan.FromSeconds(30);

    private long _lastWarningTimestamp = Stopwatch.GetTimestamp() - (long)(WarningInterval.TotalSeconds * Stopwatch.Frequency);

    public async ValueTask<RateLimitDecision> TryAcquireAsync(
        string key,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        try
        {
            RedisResult result = await connectionMultiplexer.GetDatabase().ScriptEvaluateAsync(
                AcquireScript,
                [key],
                [permitLimit, (long)window.TotalMilliseconds]);

            var values = (RedisValue[]?)result;

            if (values is not [var admitted, var retryAfterMs])
            {
                return RateLimitDecision.Admitted;
            }

            return admitted == 1
                ? RateLimitDecision.Admitted
                : RateLimitDecision.Rejected(TimeSpan.FromMilliseconds((long)retryAfterMs));
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            // **Fail open, deliberately.** A limiter that fails closed turns a cache blip into a
            // total outage of the only way into the platform — the guardrail becomes the incident.
            // The platform is less protected while Redis is down; it is not unavailable. The
            // global concurrency limit is in-process and keeps working throughout, so the ceiling
            // that actually prevents collapse is still enforced.
            WarnThrottled(exception);

            return RateLimitDecision.Admitted;
        }
    }

    private void WarnThrottled(Exception exception)
    {
        long last = Interlocked.Read(ref _lastWarningTimestamp);

        if (Stopwatch.GetElapsedTime(last) < WarningInterval ||
            Interlocked.CompareExchange(ref _lastWarningTimestamp, Stopwatch.GetTimestamp(), last) != last)
        {
            return;
        }

        logger.LogWarning(
            exception,
            "The rate-limit store is unreachable; the gateway is admitting requests without a " +
            "per-client limit until Redis recovers. The global concurrency limit is unaffected.");
    }
}
