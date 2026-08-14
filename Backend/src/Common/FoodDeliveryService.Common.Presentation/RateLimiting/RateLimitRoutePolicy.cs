using Microsoft.AspNetCore.Http;

namespace FoodDeliveryService.Common.Presentation.RateLimiting;

/// <summary>
/// The route ranking: which <see cref="RateLimitTier"/> a request belongs to.
/// <para>
/// This table <b>is</b> the shedding policy, which is why it is a table and not a chain of
/// <c>if</c>s scattered through the middleware. Reading it top to bottom tells you exactly what the
/// platform gives up first when it runs out of capacity, and a route added to the platform that is
/// not named here still lands somewhere sensible (see the fallbacks at the bottom of
/// <see cref="Classify"/>) rather than escaping the limiter.
/// </para>
/// <para>
/// Matching is on the path the <b>Gateway</b> sees — the public prefixes (<c>orders/…</c>,
/// <c>delivery/…</c>), not the downstream service's route. Segment patterns support <c>*</c> for
/// exactly one segment and <c>**</c> for the remainder.
/// </para>
/// </summary>
public static class RateLimitRoutePolicy
{
    /// <summary>
    /// Ordered — the first match wins, so the specific lifecycle routes must precede any prefix rule.
    /// A <c>null</c> method matches every verb.
    /// </summary>
    private static readonly RouteRule[] Rules =
    [
        // ── Exempt ────────────────────────────────────────────────────────────────────────────
        // Both probe paths and the aggregate. `docs/health-probe-contract.md` is the contract; the
        // blackbox exporter hits these every 15 s from outside and must never be throttled.
        new(null, "health/**", RateLimitTier.Exempt),
        new(null, "health", RateLimitTier.Exempt),
        // negotiate + connect + the WebSocket itself. See RateLimitTier.Exempt.
        new(null, "hubs/**", RateLimitTier.Exempt),

        // ── Critical: advancing work already accepted ─────────────────────────────────────────
        // The kitchen driving an order it has taken, and a customer cancelling one. A 429 here
        // leaves an order stuck in a state a human is waiting on.
        new(HttpMethods.Post, "orders/*/accept", RateLimitTier.Critical),
        new(HttpMethods.Post, "orders/*/reject", RateLimitTier.Critical),
        new(HttpMethods.Post, "orders/*/preparing", RateLimitTier.Critical),
        new(HttpMethods.Post, "orders/*/ready", RateLimitTier.Critical),
        new(HttpMethods.Post, "orders/*/cancel", RateLimitTier.Critical),
        // The delivery half of the same lifecycle. `delivered` is the single most expensive request
        // in the system to reject: the food is at the door and the platform is refusing to know.
        new(HttpMethods.Post, "delivery/deliveries/*/accept", RateLimitTier.Critical),
        new(HttpMethods.Post, "delivery/deliveries/*/reject", RateLimitTier.Critical),
        new(HttpMethods.Post, "delivery/deliveries/*/picked-up", RateLimitTier.Critical),
        new(HttpMethods.Post, "delivery/deliveries/*/delivered", RateLimitTier.Critical),
    ];

    /// <summary>
    /// The tier a request belongs to. Pure and allocation-light: called once per request, before
    /// anything else the limiter does.
    /// </summary>
    public static RateLimitTier Classify(string method, PathString path)
    {
        ReadOnlySpan<char> remaining = Trim(path.Value);

        foreach (RouteRule rule in Rules)
        {
            if ((rule.Method is null || string.Equals(rule.Method, method, StringComparison.OrdinalIgnoreCase)) &&
                Matches(rule.Pattern, remaining))
            {
                return rule.Tier;
            }
        }

        // Two fallbacks, and between them they cover every route the platform will ever add. A read
        // is a read — the cheapest thing to shed, and by volume most of the traffic. Anything that
        // mutates and was not named above creates new work rather than advancing work already in
        // flight, so Write is the safe default: a new lifecycle transition that belongs in Critical
        // earns a line in the table above, and until it does it is merely limited more tightly than
        // intended, which is the direction to fail in.
        return HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)
            ? RateLimitTier.Read
            : RateLimitTier.Write;
    }

    /// <inheritdoc cref="Classify(string, PathString)"/>
    public static RateLimitTier Classify(HttpRequest request) => Classify(request.Method, request.Path);

    /// <summary>Segment-wise match of <paramref name="pattern"/> against a trimmed request path.</summary>
    private static bool Matches(string pattern, ReadOnlySpan<char> path)
    {
        ReadOnlySpan<char> patternRemaining = pattern;

        while (!patternRemaining.IsEmpty)
        {
            ReadOnlySpan<char> patternSegment = NextSegment(ref patternRemaining);

            // `**` swallows the rest — but only if there is a rest. `hubs/**` must not match a bare
            // `hubs`, which is not a hub endpoint.
            if (patternSegment is "**")
            {
                return !path.IsEmpty;
            }

            if (path.IsEmpty)
            {
                return false;
            }

            ReadOnlySpan<char> pathSegment = NextSegment(ref path);

            if (patternSegment is not "*" &&
                !patternSegment.Equals(pathSegment, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // An exact pattern matches an exact path — `health` does not match `healthz`, and
        // `orders/*/ready` does not match a longer path that merely starts with it.
        return path.IsEmpty;
    }

    /// <summary>Takes the leading segment off <paramref name="remaining"/> and advances it past the slash.</summary>
    private static ReadOnlySpan<char> NextSegment(ref ReadOnlySpan<char> remaining)
    {
        int slash = remaining.IndexOf('/');

        if (slash < 0)
        {
            ReadOnlySpan<char> last = remaining;
            remaining = default;

            return last;
        }

        ReadOnlySpan<char> segment = remaining[..slash];
        remaining = remaining[(slash + 1)..];

        return segment;
    }

    /// <summary>
    /// Strips the leading and trailing slash so <c>/orders/</c>, <c>/orders</c> and <c>orders</c> are
    /// the same path. A trailing slash reaching the routing table as a distinct path is a classic way
    /// to walk around a path-matched guard.
    /// </summary>
    private static ReadOnlySpan<char> Trim(string? path) => path.AsSpan().Trim('/');

    private readonly record struct RouteRule(string? Method, string Pattern, RateLimitTier Tier);
}
