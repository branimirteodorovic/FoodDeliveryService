namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetMenuItem;

public sealed record MenuItemSnapshotResponse(
    Guid Id,
    Guid RestaurantId,
    string Name,
    decimal Price,
    bool IsAvailable);
