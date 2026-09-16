using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Orders.Domain.Orders;

// Carries the full placement snapshot so the integration-event handler can publish without
// querying the order back (the outbox job runs outside the placing request).
public sealed class OrderPlacedDomainEvent(
    Guid orderId,
    Guid customerId,
    Guid restaurantId,
    decimal subtotal,
    PaymentMethod paymentMethod,
    DateTime placedOnUtc) : DomainEvent
{
    public Guid OrderId { get; init; } = orderId;

    public Guid CustomerId { get; init; } = customerId;

    public Guid RestaurantId { get; init; } = restaurantId;

    public decimal Subtotal { get; init; } = subtotal;

    // Feature 3.8 Milestone F. On the event so the integration event can carry it, which is what
    // lets Payments skip cash orders without asking Orders anything.
    public PaymentMethod PaymentMethod { get; init; } = paymentMethod;

    public DateTime PlacedOnUtc { get; init; } = placedOnUtc;
}
