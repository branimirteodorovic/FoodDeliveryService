using System.Collections.Concurrent;

namespace FoodDeliveryService.Common.Presentation.RateLimiting;

/// <summary>
/// A process-local <see cref="IRateLimitStore"/> — <b>Development and tests only</b>.
/// <para>
/// It is a correct fixed-window counter for one process, and that is precisely the problem: with two
/// Gateway replicas it enforces twice the configured limit, with four it enforces four times, and it
/// never reports that it is doing so. The host that selects it logs a warning at startup for that
/// reason, and outside Development the wiring refuses to select it at all
/// (<see cref="EdgeRateLimitingExtensions"/>).
/// </para>
/// <para>
/// Mirrors the same Development-only fallback the cache and the distributed lock already have
/// (<c>docs/caching.md</c>): an unreachable Redis must not stop a developer running the stack, and
/// must not silently become a per-process guarantee anywhere else.
/// </para>
/// </summary>
public sealed class InMemoryRateLimitStore(TimeProvider? timeProvider = null) : IRateLimitStore
{
    /// <summary>
    /// Acquisitions between sweeps of expired windows.
    /// <para>
    /// A key is one client in one tier, so the dictionary would otherwise grow with every distinct
    /// caller the process has ever seen and never shrink — the same unbounded-growth failure the
    /// partitioned limiter avoids by disposing idle partitions. Piggy-backing the sweep on the call
    /// that is already happening avoids a timer and a background thread for a fallback path.
    /// </para>
    /// </summary>
    private const int SweepInterval = 1_000;

    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    private int _acquisitions;

    public ValueTask<RateLimitDecision> TryAcquireAsync(
        string key,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        long now = _timeProvider.GetUtcNow().UtcTicks;

        if (Interlocked.Increment(ref _acquisitions) % SweepInterval == 0)
        {
            Sweep(now);
        }

        Window entry = _windows.GetOrAdd(key, static _ => new Window());

        lock (entry)
        {
            // The window rolls over lazily rather than on a schedule: nothing needs to happen to a
            // partition nobody is using, and the first request after a quiet period starts a fresh
            // window from *its* arrival, which is what the Redis PEXPIRE does too.
            if (entry.ExpiresAt <= now)
            {
                entry.ExpiresAt = now + window.Ticks;
                entry.Count = 0;
            }

            entry.Count++;

            return ValueTask.FromResult(
                entry.Count <= permitLimit
                    ? RateLimitDecision.Admitted
                    : RateLimitDecision.Rejected(TimeSpan.FromTicks(entry.ExpiresAt - now)));
        }
    }

    private void Sweep(long now)
    {
        foreach (KeyValuePair<string, Window> pair in _windows)
        {
            lock (pair.Value)
            {
                // Under the window's own lock, so the expiry check and the removal cannot straddle a
                // concurrent acquisition that has just rolled it over — and the overload that takes
                // the pair removes only while the value is still this instance.
                if (pair.Value.ExpiresAt <= now)
                {
                    _windows.TryRemove(pair);
                }
            }
        }
    }

    private sealed class Window
    {
        public long ExpiresAt;
        public int Count;
    }
}
