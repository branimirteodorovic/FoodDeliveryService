using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Modules.Support.Application.Caching;

namespace FoodDeliveryService.Modules.Support.Application.Analytics.GetSupportSummary;

/// <summary>
/// The support management summary over a UTC window: <c>[FromUtc, ToUtc)</c>, half-open, so a
/// ticket cannot be counted in two adjacent windows.
/// <para>
/// The bounds are resolved and normalized by <see cref="Create"/> before the query is constructed,
/// not defaulted inside the handler, because <see cref="ICachedQuery"/> asks for the cache key
/// <em>before</em> the handler runs — a query holding nulls would key every call the same and serve
/// one window's numbers for another's.
/// </para>
/// </summary>
public sealed record GetSupportSummaryQuery(DateTime FromUtc, DateTime ToUtc)
    : ICachedQuery<SupportSummaryResponse>
{
    /// <summary>The window a caller who sends no bounds gets.</summary>
    public const int DefaultWindowInDays = 30;

    public string CacheKey => SupportCacheKeys.Summary(FromUtc, ToUtc);

    public TimeSpan? Expiration => SupportCacheKeys.SummaryExpiration;

    /// <summary>
    /// Applies the defaults and rounds both bounds down to the minute.
    /// <para>
    /// The rounding is what makes the cache work at all. An unrounded <c>utcNow</c> upper bound
    /// gives every request its own key, so the entry written by one agent's page load is never read
    /// by the next — a cache with a 100% miss rate that still costs a Redis round trip on both
    /// sides of every query.
    /// </para>
    /// </summary>
    public static GetSupportSummaryQuery Create(DateTime? from, DateTime? to, DateTime utcNow)
    {
        DateTime toUtc = to ?? utcNow;
        DateTime fromUtc = from ?? toUtc.AddDays(-DefaultWindowInDays);

        return new GetSupportSummaryQuery(TruncateToMinute(fromUtc), TruncateToMinute(toUtc));
    }

    private static DateTime TruncateToMinute(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
}
