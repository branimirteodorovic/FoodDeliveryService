using AwesomeAssertions;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;

namespace FoodDeliveryService.Modules.Payments.UnitTests;

/// <summary>
/// Feature 3.8 Milestone C, §5.2 — the first of the two rules that make or break this feature.
/// <para>
/// There is no clever assertion available here, and that is rather the point: the only property
/// worth testing about an idempotency key is that the same durable input always produces the same
/// key, because that is exactly what a <c>Guid.NewGuid()</c> or a timestamp would quietly break. A
/// key that varies per attempt looks identical in a code review and charges the customer twice on
/// the first retry.
/// </para>
/// </summary>
public class PaymentIdempotencyKeyTests
{
    private static readonly Guid OrderId = Guid.Parse("2b9a2a6e-6c4e-4f7f-9f2f-6a3a5a1c9d70");

    [Fact]
    public void EveryKey_IsStableForTheSameIdentifier()
    {
        PaymentIdempotencyKeys.Authorize(OrderId).Should().Be(PaymentIdempotencyKeys.Authorize(OrderId));
        PaymentIdempotencyKeys.Capture(OrderId).Should().Be(PaymentIdempotencyKeys.Capture(OrderId));
        PaymentIdempotencyKeys.Release(OrderId).Should().Be(PaymentIdempotencyKeys.Release(OrderId));
        PaymentIdempotencyKeys.Refund(OrderId).Should().Be(PaymentIdempotencyKeys.Refund(OrderId));
        PaymentIdempotencyKeys.Customer(OrderId).Should().Be(PaymentIdempotencyKeys.Customer(OrderId));
    }

    [Fact]
    public void TheFiveOperations_DoNotShareAKey()
    {
        // One order is authorized, captured and released, and Stripe replays the *response* it gave
        // the first time it saw a key. A shared key would make the capture return the authorization's
        // old response and the payment would look captured while the funds were still only held.
        string[] keys =
        [
            PaymentIdempotencyKeys.Authorize(OrderId),
            PaymentIdempotencyKeys.Capture(OrderId),
            PaymentIdempotencyKeys.Release(OrderId),
            PaymentIdempotencyKeys.Refund(OrderId),
            PaymentIdempotencyKeys.Customer(OrderId)
        ];

        keys.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Keys_AreScopedToTheirIdentifier()
    {
        PaymentIdempotencyKeys.Authorize(OrderId)
            .Should().NotBe(PaymentIdempotencyKeys.Authorize(Guid.NewGuid()));
    }

    [Theory]
    // The formats are fixed by §5.2 and are pinned here because they are a contract with Stripe's
    // 24-hour key store, not an implementation detail: changing one silently reopens the double
    // charge for every payment in flight at the moment of deployment.
    [InlineData("order-auth-2b9a2a6e-6c4e-4f7f-9f2f-6a3a5a1c9d70")]
    [InlineData("order-capture-2b9a2a6e-6c4e-4f7f-9f2f-6a3a5a1c9d70")]
    [InlineData("order-release-2b9a2a6e-6c4e-4f7f-9f2f-6a3a5a1c9d70")]
    [InlineData("refund-2b9a2a6e-6c4e-4f7f-9f2f-6a3a5a1c9d70")]
    [InlineData("customer-2b9a2a6e-6c4e-4f7f-9f2f-6a3a5a1c9d70")]
    public void KeyFormats_AreTheOnesTheDesignFixed(string expected)
    {
        string[] keys =
        [
            PaymentIdempotencyKeys.Authorize(OrderId),
            PaymentIdempotencyKeys.Capture(OrderId),
            PaymentIdempotencyKeys.Release(OrderId),
            PaymentIdempotencyKeys.Refund(OrderId),
            PaymentIdempotencyKeys.Customer(OrderId)
        ];

        keys.Should().Contain(expected);
    }

    [Fact]
    public void Keys_FitInsideStripesLimit()
    {
        // Stripe caps an idempotency key at 255 characters. A prefix plus a Guid is nowhere near it,
        // which is worth knowing before someone proposes appending a payload hash.
        PaymentIdempotencyKeys.Authorize(OrderId).Length.Should().BeLessThan(255);
    }
}
