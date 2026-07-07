using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.SetMenuItemAvailability;

// "Available / sold out" toggle.
public sealed record SetMenuItemAvailabilityCommand(
    Guid RestaurantId,
    Guid MenuItemId,
    bool IsAvailable) : ICommand;
