namespace FoodDeliveryService.Common.Infrastructure.Caching;

/// <summary>
/// Bound from the "Caching" appsettings section. <see cref="JitterPercentage"/> randomizes each
/// entry's TTL by up to this fraction (applied in <see cref="CacheOptions.Create"/>) so
/// mass-inserted keys don't all expire on the same tick and stampede the origin at once.
/// </summary>
public sealed class CachingSettings
{
    public const string SectionName = "Caching";

    public TimeSpan DefaultExpiration { get; init; } = TimeSpan.FromMinutes(2);

    public double JitterPercentage { get; init; } = 0.10;

    public TimeSpan ApplyJitter(TimeSpan expiration)
    {
        // Random.Shared is used for TTL jitter (cache-stampede mitigation), not for anything
        // security-sensitive — CA5394 does not apply here.
#pragma warning disable CA5394
        double jitterFactor = 1 + (Random.Shared.NextDouble() * 2 - 1) * JitterPercentage;
#pragma warning restore CA5394

        return TimeSpan.FromTicks((long)(expiration.Ticks * jitterFactor));
    }
}
