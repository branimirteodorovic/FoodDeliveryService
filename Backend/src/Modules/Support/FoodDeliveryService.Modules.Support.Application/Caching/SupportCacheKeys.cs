using FoodDeliveryService.Common.Application.Caching;

namespace FoodDeliveryService.Modules.Support.Application.Caching;

/// <summary>
/// Single source of truth for Support cache keys, built on <see cref="CacheKeys"/> so nothing is
/// concatenated at a call site — the same convention <c>RestaurantCacheKeys</c> follows.
/// <para>
/// Support has exactly one cached surface, and it is deliberately not the entity-keyed kind. There
/// is therefore no invalidation counterpart to this class: see <see cref="Expiration"/>.
/// </para>
/// </summary>
public static class SupportCacheKeys
{
    private const string Area = "support";

    /// <summary>
    /// Five minutes, and <strong>no invalidation</strong> — a deliberate departure from the
    /// inline-<c>RemoveAsync</c> rule in <c>CLAUDE.md</c>, which governs entity-keyed reads whose
    /// staleness a user experiences as a bug (a menu that still shows a withdrawn dish).
    /// <para>
    /// A 30-day management aggregate is not entity-keyed: every ticket write, message, status
    /// change and refund decision in the window would have to evict it, which is both a great deal
    /// of invalidation code and a cache that is empty whenever the queue is busy — precisely when
    /// the summary is being read. A five-minute-old support summary is the norm everywhere this
    /// number is reported, so the TTL is the whole freshness contract.
    /// </para>
    /// </summary>
    public static readonly TimeSpan SummaryExpiration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Keyed on the window, because the window is the only input: the summary is platform-wide, so
    /// two agents asking for the same fortnight must hit the same entry rather than one each. The
    /// bounds are rounded to the minute by the query before they reach here — an unrounded
    /// <c>DateTime.UtcNow</c> in a key is a cache that never gets a hit.
    /// </summary>
    public static string Summary(DateTime fromUtc, DateTime toUtc) =>
        CacheKeys.Create(Area, "summary", $"{fromUtc:O}:{toUtc:O}");
}
