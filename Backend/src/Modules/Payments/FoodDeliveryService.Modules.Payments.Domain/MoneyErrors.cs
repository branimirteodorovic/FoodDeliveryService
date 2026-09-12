using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.Domain;

public static class MoneyErrors
{
    // Zero is not an error (see Money.Create). Negative is: a negative charge is a refund taken
    // through the wrong API, and a negative refund is a charge taken through the wrong API.
    public static readonly Error AmountNegative = Error.Problem(
        "Money.AmountNegative",
        "A money amount cannot be negative");

    public static readonly Error CurrencyRequired = Error.Problem(
        "Money.CurrencyRequired",
        "A money amount must name the currency it is denominated in");

    // The shape only — three ASCII letters. Checking the value against the real ISO 4217 register
    // would need that register, and the platform configures exactly one code (§0.4), which is
    // validated at boot by PaymentsOptions rather than per amount.
    public static Error CurrencyNotIso4217(string currency) => Error.Problem(
        "Money.CurrencyNotIso4217",
        $"'{currency}' is not an ISO 4217 alphabetic currency code");
}
