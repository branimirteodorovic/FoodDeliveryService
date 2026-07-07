using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Restaurants.Application.Managers.UpdateManager;

// Keeps the local replica's name in sync with UserProfileUpdatedIntegrationEvent.
public sealed record UpdateRestaurantManagerCommand(
    Guid UserId,
    string FirstName,
    string LastName) : ICommand;
