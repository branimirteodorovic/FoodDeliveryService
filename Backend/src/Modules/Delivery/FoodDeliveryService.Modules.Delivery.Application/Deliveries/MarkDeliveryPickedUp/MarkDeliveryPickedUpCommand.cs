using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.MarkDeliveryPickedUp;

// The assigned driver (the authenticated caller) collected the food from the restaurant.
public sealed record MarkDeliveryPickedUpCommand(Guid DeliveryId) : ICommand;
