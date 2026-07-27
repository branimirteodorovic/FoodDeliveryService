using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Modules.Restaurants.Application.Caching;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetRestaurant;

public sealed record GetRestaurantQuery(Guid RestaurantId) : ICachedQuery<RestaurantResponse>
{
    public string CacheKey => RestaurantCacheKeys.Detail(RestaurantId);

    public TimeSpan? Expiration => RestaurantCacheKeys.Expiration;
}
