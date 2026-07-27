using FoodDeliveryService.Common.Application.Caching;

namespace FoodDeliveryService.Modules.Restaurants.Application.Caching;

/// <summary>
/// Single source of truth for Restaurant cache keys — shared between the cached queries (reads,
/// see <see cref="ICachedQuery"/> on GetMenu/GetRestaurant/GetMenuItem) and the menu-invalidation
/// domain-event handlers (writes) so the two can never drift apart.
/// </summary>
public static class RestaurantCacheKeys
{
    private const string Area = "restaurants";

    public static readonly TimeSpan Expiration = TimeSpan.FromMinutes(5);

    public static string Menu(Guid restaurantId) => CacheKeys.Create(Area, "menu", restaurantId);

    public static string Detail(Guid restaurantId) => CacheKeys.Create(Area, "detail", restaurantId);

    public static string Item(Guid menuItemId) => CacheKeys.Create(Area, "item", menuItemId);
}
