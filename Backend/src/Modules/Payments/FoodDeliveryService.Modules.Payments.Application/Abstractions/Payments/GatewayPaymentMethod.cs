namespace FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;

/// <summary>
/// A card as the provider describes it, reduced to the four fields this platform is allowed to keep
/// (§0.5): the identifier, and brand / last four / expiry <em>for display</em>.
/// <para>
/// Everything else a Stripe <c>PaymentMethod</c> carries — the fingerprint, the issuing country, the
/// three-D-Secure support matrix, the billing details — is deliberately dropped at this boundary.
/// Mapping it through "because it is there" is how a field nobody decided to store ends up in a
/// database backup.
/// </para>
/// <para>
/// The display fields are nullable because a payment method that is not a card has none of them. The
/// SetupIntent asks for <c>card</c> and nothing else, so it should not happen; a null here is
/// therefore a fact worth carrying rather than a reason to throw.
/// </para>
/// </summary>
public sealed record GatewayPaymentMethod(
    string PaymentMethodId,
    string? Brand,
    string? Last4,
    int? ExpiryMonth,
    int? ExpiryYear);
