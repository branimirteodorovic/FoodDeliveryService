namespace FoodDeliveryService.Modules.Payments.Application.PaymentMethods.CreateSetupIntent;

/// <summary>
/// What the browser needs to confirm a card with Stripe.js.
/// <para>
/// <see cref="ClientSecret"/> is not a credential of this service's — it authorises exactly one
/// SetupIntent belonging to one customer, and it is meant to be handed to the browser. It is
/// still short-lived and single-purpose, so it is returned and never logged, never cached and never
/// put on an integration event.
/// </para>
/// </summary>
public sealed record SetupIntentResponse(string SetupIntentId, string ClientSecret);
