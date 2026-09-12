namespace FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;

/// <summary>
/// The platform's payment constants, bound from the <c>Payments</c> configuration section. Nothing
/// here is a secret — the Stripe credentials are a separate options type in the Infrastructure
/// layer, where the SDK that consumes them lives.
/// <para>
/// §5.4 puts <c>Currency</c> on <c>StripeOptions</c>. It cannot go there: the handler that turns
/// <c>Order.Subtotal</c> into a <see cref="Domain.Money"/> is an Application-layer handler, and
/// Application cannot see Infrastructure. §0.4 already named this key <c>Payments:Currency</c>, so
/// this follows §0.4 and the layering rather than §5.4's parenthetical.
/// </para>
/// </summary>
public sealed class PaymentsOptions
{
    public const string SectionName = "Payments";

    /// <summary>
    /// The one currency every amount on this platform is denominated in — ISO 4217, upper case.
    /// It is deliberately a configured constant rather than a per-restaurant or per-customer value:
    /// multi-currency means an exchange-rate policy, a settlement currency and a
    /// restaurant/customer mismatch rule, none of which exist here. <see cref="Domain.Money"/>
    /// carries a currency code so the type is honest, but exactly one value ever flows through it.
    /// </summary>
    public string Currency { get; init; } = "EUR";
}
