using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Orders.Application.Restaurants.UpsertRestaurant;

// Builds the local Restaurant replica from RestaurantRegisteredIntegrationEvent (inbox-driven,
// idempotent — hence upsert semantics).
public sealed record UpsertRestaurantCommand(
    Guid RestaurantId,
    Guid ManagerUserId,
    string Name,
    decimal CommissionRate,
    double Latitude,
    double Longitude) : ICommand;
