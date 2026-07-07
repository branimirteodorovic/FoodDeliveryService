using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Orders.Application.Restaurants.UpdateMenuItemAvailability;

// Syncs the MenuItem replica's availability flag from MenuItemAvailabilityChangedIntegrationEvent.
public sealed record UpdateMenuItemAvailabilityCommand(Guid MenuItemId, bool IsAvailable) : ICommand;
