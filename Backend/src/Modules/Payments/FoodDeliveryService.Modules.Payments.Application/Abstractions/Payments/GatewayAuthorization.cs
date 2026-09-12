using FoodDeliveryService.Modules.Payments.Domain;

namespace FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;

/// <summary>
/// Everything the gateway needs to place a hold on a card. Not named <c>…Request</c> on purpose:
/// that suffix belongs to the MassTransit request/response contracts (<c>GetUserPermissionsRequest</c>),
/// and this never crosses the broker.
/// </summary>
/// <param name="StripeCustomerId">The <c>cus_…</c> from the customer's <c>CustomerPaymentProfile</c> (§6.1).</param>
/// <param name="PaymentMethodId">
/// The <c>pm_…</c> the customer saved earlier. The charge is off-session against a card that is
/// already attached — which is what lets the whole flow hang off <c>OrderPlacedIntegrationEvent</c>
/// instead of a synchronous call back to the customer's browser.
/// </param>
/// <param name="Amount"><c>Order.Subtotal</c> in the platform currency. There is no fee model to add to it (§0.4).</param>
/// <param name="OrderId">Carried into Stripe metadata so a charge in the dashboard is traceable to an order.</param>
public sealed record GatewayAuthorization(
    string StripeCustomerId,
    string PaymentMethodId,
    Money Amount,
    Guid OrderId);
