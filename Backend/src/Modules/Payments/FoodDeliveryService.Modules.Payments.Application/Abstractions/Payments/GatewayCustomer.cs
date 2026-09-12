namespace FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;

/// <summary>
/// A Stripe customer object. The identifier is the only thing this platform keeps — everything else
/// about the customer already lives in Users, and duplicating it into a payment provider is data
/// this service would then have to keep correct.
/// </summary>
public sealed record GatewayCustomer(string StripeCustomerId);
