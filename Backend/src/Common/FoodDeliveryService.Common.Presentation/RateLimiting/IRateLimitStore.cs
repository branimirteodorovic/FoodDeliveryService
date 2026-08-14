namespace FoodDeliveryService.Common.Presentation.RateLimiting;

/// <summary>
/// The counter behind the limiter — one fixed window per partition key.
/// <para>
/// It is an interface for exactly one reason, and it is not testability: <b>the store has to be
/// shared across replicas or the limit is a lie.</b> Per-pod in-memory buckets multiply the
/// effective limit by the replica count, which is the trap
/// <c>KUBERNETES_PHASE2_PLAN.md</c> §5.4 names — a Gateway scaled to four pods with a 200/window
/// limit is really an 800/window limit, and nobody finds out until the day it matters. The shipped
/// implementation is therefore Redis-backed (<c>Gateway/RateLimiting/RedisRateLimitStore</c>);
/// <see cref="InMemoryRateLimitStore"/> exists for Development and for tests, and says so loudly at
/// startup.
/// </para>
/// <para>
/// Fixed window rather than sliding or token bucket: one <c>INCR</c> plus a conditional
/// <c>PEXPIRE</c> is a single round trip on the hot path of every request through the single public
/// entry point. A sliding window costs a sorted set per client and a read-modify-write; the burst
/// tolerance it buys is not worth that on an edge whose job is to be cheap.
/// </para>
/// </summary>
public interface IRateLimitStore
{
    /// <summary>
    /// Counts one request against <paramref name="key"/> and says whether it is admitted.
    /// </summary>
    /// <param name="key">The partition key — tier plus client, built by the limiter.</param>
    /// <param name="permitLimit">Requests allowed per window.</param>
    /// <param name="window">Window length.</param>
    /// <param name="cancellationToken">The request's own token.</param>
    ValueTask<RateLimitDecision> TryAcquireAsync(
        string key,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken = default);
}

/// <summary>The store's answer: admitted, or rejected with the wait until the window rolls over.</summary>
/// <param name="IsAdmitted">Whether the request may proceed.</param>
/// <param name="RetryAfter">
/// How long until the partition has budget again. Reported to the client as <c>Retry-After</c>, so a
/// well-behaved caller backs off by the right amount instead of guessing — the difference between a
/// guardrail and a wall.
/// </param>
public readonly record struct RateLimitDecision(bool IsAdmitted, TimeSpan RetryAfter)
{
    /// <summary>The request is admitted; nothing to wait for.</summary>
    public static RateLimitDecision Admitted { get; } = new(true, TimeSpan.Zero);

    /// <summary>The partition is out of budget for <paramref name="retryAfter"/>.</summary>
    public static RateLimitDecision Rejected(TimeSpan retryAfter) => new(false, retryAfter);
}
