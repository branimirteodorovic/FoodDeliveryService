using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.MarkDeliveryDelivered;

// The assigned driver (the authenticated caller) delivered the food to the customer.
public sealed record MarkDeliveryDeliveredCommand(Guid DeliveryId) : ICommand;
