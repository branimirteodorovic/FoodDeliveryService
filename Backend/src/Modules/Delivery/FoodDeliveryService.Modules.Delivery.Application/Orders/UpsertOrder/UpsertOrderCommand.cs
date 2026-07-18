using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Delivery.Application.Orders.UpsertOrder;

// Builds the local Order replica from OrderReadyForPickupIntegrationEvent (inbox-driven,
// idempotent — hence upsert semantics).
public sealed record UpsertOrderCommand(
    Guid OrderId,
    Guid CustomerId,
    Guid RestaurantId,
    string DeliveryStreet,
    string DeliveryCity,
    string DeliveryPostalCode,
    string DeliveryCountry,
    string? DeliveryNotes,
    double DeliveryLatitude,
    double DeliveryLongitude,
    DateTime PlacedOnUtc) : ICommand;
