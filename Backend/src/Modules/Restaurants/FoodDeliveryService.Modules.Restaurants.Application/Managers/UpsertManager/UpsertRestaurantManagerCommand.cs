using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Restaurants.Application.Managers.UpsertManager;

// Builds the local RestaurantManager replica from UserRegisteredIntegrationEvent (inbox-driven,
// idempotent — hence upsert semantics).
public sealed record UpsertRestaurantManagerCommand(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName) : ICommand;
