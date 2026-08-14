using System.Diagnostics;
using System.Threading.RateLimiting;

namespace FoodDeliveryService.Common.Presentation.RateLimiting;

/// <summary>
/// Adapts an <see cref="IRateLimitStore"/> to the <see cref="RateLimiter"/> ASP.NET Core's built-in
/// rate-limiting middleware understands, so the Redis-backed counter gets the framework's
/// <c>OnRejected</c> hook, its <c>Microsoft.AspNetCore.RateLimiting</c> meter and its lease lifetime
/// for free instead of a hand-rolled middleware re-implementing all three.
/// <para>
/// One instance exists per partition — per <c>{tier}:{client}</c> — created on demand by
/// <see cref="PartitionedRateLimiter"/> and disposed once <see cref="IdleDuration"/> says nobody has
/// used it, which is what keeps a per-client limiter from growing a partition per caller forever.
/// </para>
/// </summary>
internal sealed class StoreRateLimiter(
    IRateLimitStore store,
    string key,
    int permitLimit,
    TimeSpan window) : RateLimiter
{
    private long _lastUsedTimestamp = Stopwatch.GetTimestamp();

    public override TimeSpan? IdleDuration => Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastUsedTimestamp));

    /// <summary>
    /// Always a failed lease, and that is not a bug.
    /// <para>
    /// The store is a network call, so there is no honest synchronous answer to give here. ASP.NET
    /// Core's <c>RateLimitingMiddleware</c> tries this first as a fast path and falls through to
    /// <see cref="AcquireAsyncCore"/> when it does not succeed, so failing costs one allocation and
    /// the real decision is taken asynchronously a moment later. Returning an *acquired* lease
    /// instead would admit every request without ever asking Redis.
    /// </para>
    /// </summary>
    protected override RateLimitLease AttemptAcquireCore(int permitCount) => AsyncOnlyLease.Instance;

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(
        int permitCount,
        CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _lastUsedTimestamp, Stopwatch.GetTimestamp());

        RateLimitDecision decision = await store.TryAcquireAsync(key, permitLimit, window, cancellationToken);

        return decision.IsAdmitted ? AdmittedLease.Instance : new RejectedLease(decision.RetryAfter);
    }

    /// <summary>
    /// Not reported. The counts live in Redis and are shared across replicas, so a per-process
    /// snapshot would be a fraction of the truth presented as the whole of it — worse than the
    /// absence the framework already handles. The real numbers come from the
    /// <c>aspnetcore.rate_limiting.*</c> meter, which counts what the middleware actually did.
    /// </summary>
    public override RateLimiterStatistics? GetStatistics() => null;

    /// <summary>A lease that was never acquired and carries no advice — the synchronous fast path.</summary>
    private sealed class AsyncOnlyLease : RateLimitLease
    {
        public static readonly AsyncOnlyLease Instance = new();

        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames => [];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;

            return false;
        }
    }

    private sealed class AdmittedLease : RateLimitLease
    {
        public static readonly AdmittedLease Instance = new();

        public override bool IsAcquired => true;

        public override IEnumerable<string> MetadataNames => [];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;

            return false;
        }
    }

    /// <summary>
    /// Rejected, carrying the wait until the window rolls over. The middleware's <c>OnRejected</c>
    /// reads it back out as <see cref="MetadataName.RetryAfter"/> and turns it into the response
    /// header — the whole reason a rejection is more useful than a dropped connection.
    /// </summary>
    private sealed class RejectedLease(TimeSpan retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames => [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (string.Equals(metadataName, MetadataName.RetryAfter.Name, StringComparison.Ordinal))
            {
                metadata = retryAfter;

                return true;
            }

            metadata = null;

            return false;
        }
    }
}
