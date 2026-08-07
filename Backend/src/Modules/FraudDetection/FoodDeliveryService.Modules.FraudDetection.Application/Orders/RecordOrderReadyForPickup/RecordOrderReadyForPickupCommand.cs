using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.FraudDetection.Application.Orders.RecordOrderReadyForPickup;

/// <summary>
/// Advances the fact to ReadyForPickup and captures the drop-off coordinates.
/// <para>
/// This handler is not in the plan's Milestone A list, and it is here for one reason:
/// OrderReadyForPickup is the <b>only</b> shipped event that carries the delivery coordinates the
/// plan puts on <c>OrderFact</c>. Consuming it closes that gap with no upstream change, where the
/// alternative would have been an additive field on OrderPlaced. It also carries the full order
/// snapshot, so unlike the other lifecycle handlers it can build the fact row from scratch.
/// </para>
/// </summary>
public sealed record RecordOrderReadyForPickupCommand(
    Guid OrderId,
    Guid CustomerId,
    Guid RestaurantId,
    decimal Subtotal,
    DateTime PlacedOnUtc,
    double DropoffLatitude,
    double DropoffLongitude,
    DateTime ReadyOnUtc) : ICommand;
