namespace FoodDeliveryService.Common.Presentation.RateLimiting;

/// <summary>
/// How expensive it is to reject a request — the ranking that makes load shedding *shaped* rather
/// than uniform.
/// <para>
/// A limiter that sheds every route equally protects the platform and ruins it at the same time: a
/// <c>429</c> on <c>GET restaurants</c> is a slightly worse browse and the customer retries in a
/// second, while a <c>429</c> on <c>POST delivery/deliveries/{id}/delivered</c> strands a delivery
/// that has already happened in the real world — the food is at the door and the platform refuses to
/// record it. The tiers below are that difference, expressed as something the limiter can act on.
/// </para>
/// </summary>
public enum RateLimitTier
{
    /// <summary>
    /// Never limited, in any partition, at any load.
    /// <para>
    /// Health probes (<c>/health/live</c>, <c>/health/ready</c>, <c>/health</c>) because the blackbox
    /// exporter probes every host every 15 s and a throttled probe is a *false outage alarm* — the
    /// limiter would manufacture the incident it exists to prevent. The SignalR paths
    /// (<c>hubs/**</c>) because negotiate-then-connect is one logical connection across two requests,
    /// and because a long-lived WebSocket held in a concurrency slot would exhaust the global limit
    /// with clients that are idle by design.
    /// </para>
    /// </summary>
    Exempt,

    /// <summary>
    /// Advancing work the platform has already accepted: order lifecycle transitions and the delivery
    /// accept/pick-up/deliver path. Shed <b>last</b> — these carry the most generous per-client budget
    /// and bypass the global concurrency limit entirely, so browsing is what gets sacrificed to keep
    /// in-flight orders and deliveries closing.
    /// </summary>
    Critical,

    /// <summary>
    /// Creating new work or reporting state: <c>POST orders</c>, driver location and availability,
    /// registration, catalogue administration. A rejection here costs a retry and nothing is stranded
    /// — the order was never placed — so this is the tightest per-client budget in the system.
    /// </summary>
    Write,

    /// <summary>
    /// Every read. The highest-volume traffic in the mix by a wide margin (browse is 70% of the load
    /// model) and the cheapest thing to lose, so it is shed first when the platform runs out of
    /// capacity.
    /// </summary>
    Read,
}
