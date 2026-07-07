using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.UpdateMenuItem;

public sealed record UpdateMenuItemCommand(
    Guid RestaurantId,
    Guid MenuItemId,
    string Name,
    string Description,
    decimal Price,
    string? PhotoUrl) : ICommand;
