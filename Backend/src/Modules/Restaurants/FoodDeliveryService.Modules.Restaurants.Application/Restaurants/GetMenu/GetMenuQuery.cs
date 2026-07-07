using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetMenu;

public sealed record GetMenuQuery(Guid RestaurantId) : IQuery<MenuResponse>;
