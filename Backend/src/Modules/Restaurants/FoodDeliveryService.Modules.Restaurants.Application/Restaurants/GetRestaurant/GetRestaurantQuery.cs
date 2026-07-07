using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetRestaurant;

public sealed record GetRestaurantQuery(Guid RestaurantId) : IQuery<RestaurantResponse>;
