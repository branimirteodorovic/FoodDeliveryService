namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetMenu;

// Full menu (categories + items) for the storefront.
public sealed record MenuResponse(Guid RestaurantId, IReadOnlyCollection<MenuCategoryResponse> Categories);

public sealed record MenuCategoryResponse(
    Guid Id,
    string Name,
    int DisplayOrder,
    IReadOnlyCollection<MenuItemResponse> Items);

public sealed record MenuItemResponse(
    Guid Id,
    string Name,
    string Description,
    decimal Price,
    string? PhotoUrl,
    bool IsAvailable);
