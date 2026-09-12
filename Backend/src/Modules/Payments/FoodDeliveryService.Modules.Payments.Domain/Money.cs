using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.Domain;

/// <summary>
/// An amount and the currency it is denominated in — the first money type in this repository.
/// Every other price on the platform (<c>Order.Subtotal</c>, <c>MenuItem.UnitPrice</c>,
/// <c>OrderLine.LineTotal</c>) is a bare <see cref="decimal"/> whose currency is implied by there
/// being only one, which is survivable for a number that is displayed and fatal for a number that
/// is handed to a payment provider.
/// <para>
/// The plan (§5.1) sketches this as a positional record. It is written with a private constructor
/// instead, for the same reason every aggregate here has one: a public primary constructor is a
/// second way in that skips <see cref="Create"/>, and the whole point of the type is that a
/// negative amount or a currency Stripe will reject cannot be constructed at all.
/// </para>
/// </summary>
public sealed record Money
{
    /// <summary>
    /// The exponent every currency this platform uses shares: 100 minor units to the major unit.
    /// It is hard-coded because §0.4 fixes the platform to exactly one currency. The zero-decimal
    /// currencies (JPY, KRW, VND) and the three-decimal ones (BHD, KWD, TND) do not divide by 100,
    /// so supporting them means a currency→exponent table here and a matching change in
    /// <see cref="ToMinorUnits"/>. Building that table now would be building something nothing
    /// exercises.
    /// </summary>
    private const decimal MinorUnitsPerMajorUnit = 100m;

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    /// <summary>The amount in major units — 12.99, not 1299.</summary>
    public decimal Amount { get; }

    /// <summary>An upper-case ISO 4217 alphabetic code, e.g. <c>EUR</c>.</summary>
    public string Currency { get; }

    /// <summary>
    /// The only way to build a <see cref="Money"/>. Zero is allowed deliberately: a running total of
    /// "refunds settled so far" starts there, and refusing it would force every such caller into a
    /// nullable. A charge being strictly positive is the <c>Payment</c> aggregate's rule, not this
    /// type's.
    /// </summary>
    public static Result<Money> Create(decimal amount, string currency)
    {
        if (amount < 0m)
        {
            return Result.Failure<Money>(MoneyErrors.AmountNegative);
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            return Result.Failure<Money>(MoneyErrors.CurrencyRequired);
        }

        string normalized = currency.Trim().ToUpperInvariant();

        if (normalized.Length != 3 || !normalized.All(char.IsAsciiLetterUpper))
        {
            return Result.Failure<Money>(MoneyErrors.CurrencyNotIso4217(currency));
        }

        return Result.Success(new Money(amount, normalized));
    }

    /// <summary>
    /// The integer minor-unit amount Stripe's API takes — 12.99 EUR is <c>1299</c>.
    /// <para>
    /// The rounding is explicit and away from zero, and that is the whole reason this method exists
    /// rather than a cast at each call site. An implicit <c>(long)</c> truncates towards zero, so
    /// 12.99 × 100 held as 1298.9999… becomes 1298 and the customer is undercharged by a cent on
    /// every order — a defect that reconciles to a rounding error rather than to a bug report.
    /// </para>
    /// </summary>
    public long ToMinorUnits() =>
        (long)Math.Round(Amount * MinorUnitsPerMajorUnit, MidpointRounding.AwayFromZero);
}
