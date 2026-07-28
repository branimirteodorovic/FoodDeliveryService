namespace FoodDeliveryService.Common.Application.Locking;

/// <summary>
/// A short-lived, best-effort mutual exclusion primitive shared by every instance of a service —
/// the cross-process equivalent of a <c>lock</c> block, for critical sections that a single-process
/// lock cannot protect (multiple replicas, a background job racing an HTTP request).
/// <para>
/// The contract is deliberately non-blocking: <see cref="TryAcquireAsync"/> either wins the
/// resource immediately or returns <see langword="null"/>. Callers decide what a loss means —
/// skipping idempotent work, moving to the next candidate, or failing so an outer retry (inbox,
/// Quartz job) re-runs the whole operation later. Nobody queues, so a lock holder can never stall
/// a request thread.
/// </para>
/// <para>
/// Every acquisition carries a time-to-live: the lock releases itself if the holder crashes before
/// disposing the handle. That makes the TTL a correctness parameter — it must comfortably exceed
/// the critical section, yet stay short enough that a crashed holder doesn't block progress for
/// long. Disposing the handle releases the lock only if it is still the caller's own (a lapsed
/// holder must never delete the lock someone else has since taken).
/// </para>
/// </summary>
public interface IDistributedLock
{
    /// <summary>
    /// Attempts to take <paramref name="resource"/> for at most <paramref name="ttl"/>. Returns a
    /// handle to release it (dispose it, ideally with <c>await using</c>), or <see langword="null"/>
    /// when another caller currently holds it.
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireAsync(
        string resource,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);
}
