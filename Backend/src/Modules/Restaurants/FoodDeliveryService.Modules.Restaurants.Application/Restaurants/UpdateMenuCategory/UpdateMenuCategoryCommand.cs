using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.UpdateMenuCategory;

public sealed record UpdateMenuCategoryCommand(
    Guid RestaurantId,
    Guid CategoryId,
    string Name,
    int DisplayOrder) : ICommand;
