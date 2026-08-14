namespace FoodDeliveryService.Common.Presentation.RateLimiting;

/// <summary>
/// The edge limiter's numbers, from the <c>"RateLimiting"</c> configuration section.
/// <para>
/// Every one of them is tunable because every one of them is environment-specific: the defaults are
/// derived from what Feature 3.5 Milestone F measured on an 8-core compose host
/// (<c>docs/load-testing.md</c>), and a machine with four times the cores wants four times the
/// concurrency. They are defaults, not constants — but they are defaults that *bind*, because a
/// limiter configured so loosely it never fires is indistinguishable from the one this replaced.
/// </para>
/// </summary>
public sealed class EdgeRateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// On by default. The kill switch exists for a bad afternoon, not as a deployment choice — an
    /// edge with no admission control is the state Milestone G was written to end.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Requests allowed to be in flight through the Gateway at once, across every client, for
    /// everything except <see cref="RateLimitTier.Exempt"/> and <see cref="RateLimitTier.Critical"/>.
    /// <para>
    /// <b>This is the number that turns a cliff into a plateau</b>, and the per-client windows are
    /// not. Past the knee it is not one client misbehaving, it is every client arriving at once, and
    /// a per-client budget none of them individually exceed does nothing about that. A bounded queue
    /// of work in flight is what makes the platform serve most requests well instead of serving all
    /// of them badly.
    /// </para>
    /// <para>
    /// The default is Little's law over the round-one measurements: at the knee (20 customers/s) the
    /// reference run sustained ~50 requests/s at ~213 ms average, which is ~11 requests in flight.
    /// 48 leaves roughly 4× headroom, so ordinary bursts pass untouched and sustained overload —
    /// where latency climbs and concurrency climbs with it — is what gets shed.
    /// </para>
    /// </summary>
    public int GlobalConcurrencyLimit { get; set; } = 48;

    /// <summary>
    /// Queue depth in front of the concurrency limit. <b>Zero on purpose.</b> Queuing at the edge
    /// converts a fast rejection into a slow one: the client waits, times out anyway, and the
    /// platform did the work of holding the connection for nothing. Shedding is only useful if it is
    /// immediate.
    /// </summary>
    public int GlobalQueueLimit { get; set; }

    /// <summary>Fixed-window length for the per-client budgets below.</summary>
    public int WindowSeconds { get; set; } = 10;

    /// <summary>
    /// Reads per client per window. 200 in 10 s is 20 requests/second sustained from one account —
    /// two orders of magnitude above a real browse (list, detail, menu with think time is ~0.3/s) and
    /// still low enough to stop a scraper. It is deliberately above what the load harness's own
    /// dispatch-board polling does on a single shared admin token, which is the one client in the
    /// system that legitimately behaves like an abusive one; see <c>loadtest/README.md</c>.
    /// </summary>
    public int ReadPermitLimit { get; set; } = 200;

    /// <summary>
    /// Writes per client per window. Tightest budget in the system: 6/second sustained is far more
    /// than a human places orders at, and a rejected write costs a retry rather than stranding
    /// anything.
    /// </summary>
    public int WritePermitLimit { get; set; } = 60;

    /// <summary>
    /// Lifecycle transitions per client per window. Effectively unlimited for a real actor — a
    /// driver completing a delivery makes three of these — and it is generous on purpose: this tier
    /// exists to be the last thing shed, so its budget is a backstop against a broken client looping,
    /// not a capacity control.
    /// </summary>
    public int CriticalPermitLimit { get; set; } = 300;

    /// <summary>
    /// <c>Retry-After</c>, in seconds, when the rejection carries no window to wait for — a
    /// concurrency rejection, where the honest answer is "almost immediately": the slot frees when
    /// whichever request is in front finishes, typically in milliseconds.
    /// </summary>
    public int DefaultRetryAfterSeconds { get; set; } = 1;

    /// <summary>
    /// Namespaces the counter keys in the shared Redis, which also carries the cache, the distributed
    /// lock, the driver GEO set and the SignalR backplane (<c>docs/caching.md</c> §1).
    /// </summary>
    public string KeyPrefix { get; set; } = "ratelimit";

    /// <summary>Per-client budget for a tier.</summary>
    public int PermitLimitFor(RateLimitTier tier) => tier switch
    {
        RateLimitTier.Critical => CriticalPermitLimit,
        RateLimitTier.Write => WritePermitLimit,
        _ => ReadPermitLimit,
    };

    /// <summary>The fixed window, as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan Window => TimeSpan.FromSeconds(Math.Max(WindowSeconds, 1));
}
