using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.CreateMenuCategory;

public sealed record CreateMenuCategoryCommand(
    Guid RestaurantId,
    string Name,
    int DisplayOrder) : ICommand<Guid>;
