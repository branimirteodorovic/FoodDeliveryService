using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.CreateDelivery;

// Creates the Delivery aggregate for an order that just became ready for pickup and starts the
// offer routine. Inbox-driven (idempotent on OrderId) — a redelivered event finds the existing
// delivery and simply re-enters the assignment routine, which is itself a no-op unless the
// delivery is still Pending.
public sealed record CreateDeliveryCommand(
    Guid OrderId,
    Guid RestaurantId,
    Guid CustomerId,
    double PickupLatitude,
    double PickupLongitude,
    string DropoffStreet,
    string DropoffCity,
    string DropoffPostalCode,
    string DropoffCountry,
    string? DropoffNotes,
    double DropoffLatitude,
    double DropoffLongitude) : ICommand;
