namespace FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;

/// <summary>
/// A SetupIntent, created so the browser can collect a card with Stripe.js and confirm it directly
/// against Stripe — which is what keeps this backend at SAQ-A (§0.5): the PAN never comes here.
/// <para>
/// <see cref="ClientSecret"/> is handed to the browser and is not a credential of this service's;
/// it authorises exactly one SetupIntent and nothing else. The attachment itself is not this call —
/// it is the <c>setup_intent.succeeded</c> webhook (§7).
/// </para>
/// </summary>
public sealed record GatewaySetupIntent(string SetupIntentId, string ClientSecret);
