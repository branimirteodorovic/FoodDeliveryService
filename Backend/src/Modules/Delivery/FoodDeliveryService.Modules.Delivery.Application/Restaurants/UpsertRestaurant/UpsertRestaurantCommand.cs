using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Delivery.Application.Restaurants.UpsertRestaurant;

// Builds the local Restaurant replica from RestaurantRegistered / RestaurantAddressUpdated
// integration events (inbox-driven, idempotent — hence upsert semantics).
public sealed record UpsertRestaurantCommand(
    Guid RestaurantId,
    string Name,
    double Latitude,
    double Longitude) : ICommand;
