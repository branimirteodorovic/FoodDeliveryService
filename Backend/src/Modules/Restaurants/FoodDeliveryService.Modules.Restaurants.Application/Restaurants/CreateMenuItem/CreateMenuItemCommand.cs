using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.CreateMenuItem;

public sealed record CreateMenuItemCommand(
    Guid RestaurantId,
    Guid CategoryId,
    string Name,
    string Description,
    decimal Price,
    string? PhotoUrl,
    bool IsAvailable) : ICommand<Guid>;
