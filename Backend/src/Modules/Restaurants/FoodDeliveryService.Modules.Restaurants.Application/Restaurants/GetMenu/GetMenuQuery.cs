using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Modules.Restaurants.Application.Caching;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetMenu;

public sealed record GetMenuQuery(Guid RestaurantId) : ICachedQuery<MenuResponse>
{
    public string CacheKey => RestaurantCacheKeys.Menu(RestaurantId);

    public TimeSpan? Expiration => RestaurantCacheKeys.Expiration;
}
