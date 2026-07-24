using Microsoft.Extensions.Caching.Distributed;

namespace FoodDeliveryService.Common.Infrastructure.Caching;

public static class CacheOptions
{
    public static DistributedCacheEntryOptions Create(TimeSpan? expiration, CachingSettings settings) =>
        new()
        {
            AbsoluteExpirationRelativeToNow = settings.ApplyJitter(expiration ?? settings.DefaultExpiration)
        };
}
