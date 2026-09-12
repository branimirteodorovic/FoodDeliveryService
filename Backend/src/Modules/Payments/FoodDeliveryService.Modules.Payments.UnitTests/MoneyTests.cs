using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Domain;

namespace FoodDeliveryService.Modules.Payments.UnitTests;

/// <summary>
/// The repository's first money type — Feature 3.8 Milestone C, §5.1.
/// <para>
/// The rounding cases are the ones that matter and they are the reason the type exists. Stripe takes
/// integer minor units, the platform holds prices as <c>decimal</c> with two places, and the obvious
/// conversion — a cast — truncates towards zero. On 12.99 that is a cent short on every single
/// order, which reconciles as a rounding discrepancy rather than as a bug, so nothing ever
/// investigates it.
/// </para>
/// </summary>
public class MoneyTests
{
    private const string Currency = "EUR";

    [Theory]
    // The everyday cases, and 12.99 is the one a cast gets wrong.
    [InlineData(12.99, 1299)]
    [InlineData(0, 0)]
    [InlineData(1, 100)]
    [InlineData(0.01, 1)]
    [InlineData(42.50, 4250)]
    [InlineData(999999.99, 99999999)]
    // The x.xx5 boundary §13.1 asks for. MidpointRounding.AwayFromZero, not .NET's banker's
    // rounding default: with ToEven, 12.345 rounds down and 12.355 rounds up in a way that is
    // correct on average and indefensible on a single receipt. Charging the half-cent up is the
    // conventional and defensible direction.
    [InlineData(12.345, 1235)]
    [InlineData(12.355, 1236)]
    [InlineData(0.005, 1)]
    [InlineData(0.015, 2)]
    public void ToMinorUnits_Converts(decimal amount, long expected)
    {
        Money money = Money.Create(amount, Currency).Value;

        money.ToMinorUnits().Should().Be(expected);
    }

    [Fact]
    public void Create_UpperCasesTheCurrency()
    {
        Money money = Money.Create(10m, "eur").Value;

        // Stripe wants it lower-case on the wire and ISO 4217 writes it upper-case. The type stores
        // one form so two amounts that mean the same thing compare equal.
        money.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Create_TrimsSurroundingWhitespace()
    {
        Money money = Money.Create(10m, " EUR ").Value;

        money.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Create_Should_Fail_WhenAmountIsNegative()
    {
        Result<Money> result = Money.Create(-0.01m, Currency);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MoneyErrors.AmountNegative);
    }

    [Fact]
    public void Create_Should_Succeed_WhenAmountIsZero()
    {
        // Not an oversight: "refunds settled so far" starts at zero, and a Money that cannot be zero
        // forces every such running total into a nullable. A charge being strictly positive is the
        // Payment aggregate's rule (§8), not this type's.
        Money.Create(0m, Currency).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_Should_Fail_WhenCurrencyIsBlank(string currency)
    {
        Result<Money> result = Money.Create(10m, currency);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MoneyErrors.CurrencyRequired);
    }

    [Theory]
    [InlineData("EU")]
    [InlineData("EURO")]
    [InlineData("E1R")]
    [InlineData("€")]
    public void Create_Should_Fail_WhenCurrencyIsNotAThreeLetterCode(string currency)
    {
        Result<Money> result = Money.Create(10m, currency);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MoneyErrors.CurrencyNotIso4217(currency).Code);
    }

    [Fact]
    public void TwoAmountsInDifferentCurrencies_AreNotEqual()
    {
        // The whole reason the currency is on the type. Two bare decimals of 10 are equal; ten euros
        // and ten dollars are not, and a value type that says otherwise is how a currency bug
        // survives a code review.
        Money euros = Money.Create(10m, "EUR").Value;
        Money dollars = Money.Create(10m, "USD").Value;

        euros.Should().NotBe(dollars);
        euros.Should().Be(Money.Create(10m, "EUR").Value);
    }
}
