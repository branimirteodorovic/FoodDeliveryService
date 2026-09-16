using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Domain;
using FoodDeliveryService.Modules.Payments.Domain.Payments;

namespace FoodDeliveryService.Modules.Payments.UnitTests;

/// <summary>
/// The payment state machine — Feature 3.8 Milestone F, §8.1 and §8.6.
/// <para>
/// Most of these cases are about what the aggregate refuses to do, and that is deliberate. Two
/// processes drive these transitions — the outbox handler acting on the placed order and the webhook
/// arm acting on the provider's own account of the same charge — neither is ordered with respect to
/// the other, and both are at-least-once. Every "returns success without acting" below is therefore
/// a charge that did not happen twice, or an order that did not get cancelled after its money was
/// already held.
/// </para>
/// </summary>
public class PaymentTests
{
    private static readonly DateTime CreatedOn = new(2026, 9, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AuthorizedOn = new(2026, 9, 15, 9, 0, 1, DateTimeKind.Utc);

    private const string PaymentIntentId = "pi_test_authorize";

    [Fact]
    public void Start_Should_RecordTheIntentToCharge_WithoutAnnouncingAnything()
    {
        // Act
        Payment payment = NewPayment();

        // Assert — the row that exists before a single provider call, which is what the reconciling
        // webhook has to find when a response goes missing.
        payment.Status.Should().Be(PaymentStatus.Authorizing);
        payment.StripePaymentIntentId.Should().BeNull();
        payment.IsTerminal.Should().BeFalse();
        payment.CreatedOnUtc.Should().Be(CreatedOn);
        payment.DomainEvents.Should().BeEmpty(
            "an authorization nobody has attempted yet is not news to any other service");
    }

    [Fact]
    public void Start_Should_RefuseAZeroAmount()
    {
        // Arrange — zero is the only non-positive amount that gets this far: Money refuses a
        // negative one outright, and allows zero because a running refund total starts there. So
        // "a charge must be strictly positive" is genuinely this aggregate's rule to enforce, and
        // this is the only place it can be.
        Money money = Money.Create(0m, "EUR").Value;

        // Act
        Result<Payment> result = Payment.Start(Guid.CreateVersion7(), Guid.NewGuid(), Guid.NewGuid(), money, CreatedOn);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PaymentErrors.AmountNotPositive);
    }

    [Fact]
    public void Authorize_Should_HoldTheFunds_AndRaiseTheEvent()
    {
        // Arrange
        Payment payment = NewPayment();

        // Act
        Result result = payment.Authorize(PaymentIntentId, AuthorizedOn);

        // Assert
        result.IsSuccess.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Authorized);
        payment.StripePaymentIntentId.Should().Be(PaymentIntentId);
        payment.AuthorizedOnUtc.Should().Be(AuthorizedOn);
        payment.IsTerminal.Should().BeFalse("an authorization is a hold, and a hold is not the end");

        PaymentAuthorizedDomainEvent raised =
            payment.DomainEvents.OfType<PaymentAuthorizedDomainEvent>().Single();

        raised.OrderId.Should().Be(payment.OrderId);
        raised.Amount.Should().Be(payment.Amount.Amount);
        raised.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Authorize_Should_BeANoOp_WhenTheSameIntentArrivesTwice()
    {
        // Arrange — the ordinary case: the API response authorized the payment, and Stripe's own
        // amount_capturable_updated webhook says the same thing a moment later.
        Payment payment = NewPayment();
        payment.Authorize(PaymentIntentId, AuthorizedOn);
        payment.ClearDomainEvents();

        // Act
        Result result = payment.Authorize(PaymentIntentId, AuthorizedOn.AddSeconds(5));

        // Assert
        result.IsSuccess.Should().BeTrue();
        payment.AuthorizedOnUtc.Should().Be(AuthorizedOn, "the second account of one hold is not a second hold");
        payment.DomainEvents.Should().BeEmpty(
            "a second event would have Orders re-project a change that did not happen");
    }

    [Fact]
    public void Authorize_Should_RefuseASecondDifferentIntent()
    {
        // Arrange
        Payment payment = NewPayment();
        payment.Authorize(PaymentIntentId, AuthorizedOn);

        // Act
        Result result = payment.Authorize("pi_test_a_different_one", AuthorizedOn.AddSeconds(5));

        // Assert — two intents for one order means two holds on one card. Overwriting the id would
        // lose the one that is still capturable, so this is refused rather than reconciled.
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PaymentErrors.PaymentIntentMismatch(payment.OrderId));
        payment.StripePaymentIntentId.Should().Be(PaymentIntentId);
    }

    [Fact]
    public void Authorize_Should_RefuseAnEmptyIntentIdentifier()
    {
        // Act
        Result result = NewPayment().Authorize("   ", AuthorizedOn);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PaymentErrors.PaymentIntentRequired);
    }

    [Fact]
    public void Fail_Should_RecordTheReason_AndRaiseTheEvent()
    {
        // Arrange
        Payment payment = NewPayment();

        // Act
        Result result = payment.Fail(PaymentFailureReason.InsufficientFunds, PaymentIntentId, AuthorizedOn);

        // Assert
        result.IsSuccess.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.FailureReason.Should().Be(PaymentFailureReason.InsufficientFunds);
        payment.FailedOnUtc.Should().Be(AuthorizedOn);
        payment.IsTerminal.Should().BeTrue();

        // The provider's id is kept even though nothing was charged: it is what an operator quotes
        // in the Stripe dashboard when a customer asks what happened.
        payment.StripePaymentIntentId.Should().Be(PaymentIntentId);

        PaymentAuthorizationFailedDomainEvent raised =
            payment.DomainEvents.OfType<PaymentAuthorizationFailedDomainEvent>().Single();

        raised.OrderId.Should().Be(payment.OrderId);
        raised.Reason.Should().Be(PaymentFailureReason.InsufficientFunds);
    }

    [Fact]
    public void Fail_Should_RefuseAReasonOutsideTheBoundedSet()
    {
        // Arrange — the reason reaches a metric tag and a customer-facing email, so the bound is
        // checked here rather than trusted of the caller that maps the provider's vocabulary.
        Payment payment = NewPayment();

        // Act
        Result result = payment.Fail("Your card was declined. Please contact your bank.", null, AuthorizedOn);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(
            PaymentErrors.FailureReasonUnknown("Your card was declined. Please contact your bank."));
        payment.Status.Should().Be(PaymentStatus.Authorizing);
    }

    [Fact]
    public void Fail_Should_BeANoOp_WhenThePaymentIsAlreadyAuthorized()
    {
        // Arrange — the case §8.6 exists for. A payment_failed webhook about an earlier attempt,
        // arriving after the hold succeeded, must not cancel an order whose money is on a card.
        Payment payment = NewPayment();
        payment.Authorize(PaymentIntentId, AuthorizedOn);
        payment.ClearDomainEvents();

        // Act
        Result result = payment.Fail(PaymentFailureReason.CardDeclined, PaymentIntentId, AuthorizedOn.AddSeconds(5));

        // Assert
        result.IsSuccess.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Authorized);
        payment.FailureReason.Should().BeNull();
        payment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Fail_Should_BeANoOp_WhenTheSameFailureArrivesTwice()
    {
        // Arrange
        Payment payment = NewPayment();
        payment.Fail(PaymentFailureReason.CardDeclined, null, AuthorizedOn);
        payment.ClearDomainEvents();

        // Act
        Result result = payment.Fail(PaymentFailureReason.ExpiredCard, null, AuthorizedOn.AddSeconds(5));

        // Assert — the first refusal stands. A redelivery must not restate the reason, or the
        // customer's email and the platform's own count would disagree with each other.
        result.IsSuccess.Should().BeTrue();
        payment.FailureReason.Should().Be(PaymentFailureReason.CardDeclined);
        payment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Authorize_Should_BeANoOp_WhenThePaymentHasAlreadyFailed()
    {
        // Arrange — the mirror of the case above, and the one that would otherwise resurrect an
        // order the platform has already cancelled.
        Payment payment = NewPayment();
        payment.Fail(PaymentFailureReason.CardDeclined, null, AuthorizedOn);
        payment.ClearDomainEvents();

        // Act
        Result result = payment.Authorize(PaymentIntentId, AuthorizedOn.AddSeconds(5));

        // Assert
        result.IsSuccess.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.DomainEvents.Should().BeEmpty();
    }

    private static Payment NewPayment() => Payment.Start(
        Guid.CreateVersion7(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Money.Create(24.99m, "EUR").Value,
        CreatedOn).Value;
}
