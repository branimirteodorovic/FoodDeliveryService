using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetRestaurant;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetRestaurants;

// Minimal paged browse — search/filter arrives with the ordering work.
public sealed record GetRestaurantsQuery(int Page, int PageSize) : IQuery<IReadOnlyCollection<RestaurantResponse>>;
