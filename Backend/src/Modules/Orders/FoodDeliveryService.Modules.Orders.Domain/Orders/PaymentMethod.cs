namespace FoodDeliveryService.Modules.Orders.Domain.Orders;

public enum PaymentMethod
{
    /// <summary>Paid to the courier at the door. The order's <see cref="PaymentStatus"/> is <c>NotRequired</c> for its whole life.</summary>
    CashOnDelivery = 1,

    /// <summary>
    /// Charged to a card the customer saved earlier — Feature 3.8 Milestone F, §8.2.
    /// <para>
    /// The card itself lives at the payment provider and this module never sees it: all Orders holds
    /// is a one-flag replica saying the customer has one (<c>CustomerPaymentProfile.CanPayByCard</c>),
    /// and an orthogonal <see cref="PaymentStatus"/> saying where the money got to. The authorization
    /// happens in the Payments service, off the back of <c>OrderPlacedIntegrationEvent</c>.
    /// </para>
    /// </summary>
    Card = 2
}
