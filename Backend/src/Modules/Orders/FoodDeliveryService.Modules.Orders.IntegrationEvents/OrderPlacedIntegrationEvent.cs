using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Orders.IntegrationEvents;

public sealed class OrderPlacedIntegrationEvent : IntegrationEvent
{
    public OrderPlacedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid orderId,
        Guid customerId,
        Guid restaurantId,
        decimal subtotal,
        string paymentMethod,
        DateTime placedOnUtc)
        : base(id, occurredOnUtc)
    {
        OrderId = orderId;
        CustomerId = customerId;
        RestaurantId = restaurantId;
        Subtotal = subtotal;
        PaymentMethod = paymentMethod;
        PlacedOnUtc = placedOnUtc;
    }

    public Guid OrderId { get; init; }

    public Guid CustomerId { get; init; }

    public Guid RestaurantId { get; init; }

    public decimal Subtotal { get; init; }

    /// <summary>
    /// One of <see cref="OrderPaymentMethods"/> — Feature 3.8 Milestone F.
    /// <para>
    /// Added so Payments can skip cash orders <em>entirely</em> (§8.1): no <c>Payment</c> row, no
    /// provider call, nothing. Without it the only way to tell the two apart would be to ask Orders,
    /// which is the synchronous cross-service call this feature was designed to avoid — a full
    /// snapshot means a consumer never needs to call back (hard rule #9), and "is there money to
    /// collect?" turned out to be missing from the snapshot.
    /// </para>
    /// </summary>
    public string PaymentMethod { get; init; }

    public DateTime PlacedOnUtc { get; init; }
}
