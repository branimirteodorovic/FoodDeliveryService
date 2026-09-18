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

    private static readonly DateTime CapturedOn = new(2026, 9, 15, 9, 4, 0, DateTimeKind.Utc);

    private static readonly DateTime RefundedOn = new(2026, 9, 16, 11, 0, 0, DateTimeKind.Utc);

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

    [Fact]
    public void Capture_Should_TakeTheMoney_AndRaiseTheEvent()
    {
        // Arrange
        Payment payment = Authorized();

        // Act
        Result result = payment.Capture(CapturedOn);

        // Assert
        result.IsSuccess.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Captured);
        payment.CapturedOnUtc.Should().Be(CapturedOn);
        payment.IsTerminal.Should().BeTrue("a captured payment only moves again by a refund, which is a new decision");

        PaymentCapturedDomainEvent raised =
            payment.DomainEvents.OfType<PaymentCapturedDomainEvent>().Single();

        raised.OrderId.Should().Be(payment.OrderId);
        raised.Amount.Should().Be(payment.Amount.Amount);
        raised.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Capture_Should_BeANoOp_WhenTheCaptureArrivesTwice()
    {
        // Arrange — the ordinary case of the reconciling arm: the capture call already recorded it,
        // and payment_intent.succeeded says the same thing a moment later. A second event here is a
        // second charge as far as every consumer downstream can tell.
        Payment payment = Authorized();
        payment.Capture(CapturedOn);
        payment.ClearDomainEvents();

        // Act
        Result result = payment.Capture(CapturedOn.AddSeconds(5));

        // Assert
        result.IsSuccess.Should().BeTrue();
        payment.CapturedOnUtc.Should().Be(CapturedOn);
        payment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Capture_Should_BeANoOp_WhileThePaymentIsStillAuthorizing()
    {
        // Arrange — there is no hold to take yet. Unreachable in practice, because Order.Accept()
        // refuses until the authorization has been projected back, and absorbed rather than refused
        // because the alternative is an error on an inbox row nothing can act on.
        Payment payment = NewPayment();

        // Act
        Result result = payment.Capture(CapturedOn);

        // Assert
        result.IsSuccess.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Authorizing);
        payment.CapturedOnUtc.Should().BeNull();
        payment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Release_Should_GiveUpTheHold_AndRaiseTheEvent()
    {
        // Arrange
        Payment payment = Authorized();

        // Act
        Result result = payment.Release(CapturedOn);

        // Assert
        result.IsSuccess.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Released);
        payment.ReleasedOnUtc.Should().Be(CapturedOn);
        payment.IsTerminal.Should().BeTrue();

        PaymentReleasedDomainEvent raised =
            payment.DomainEvents.OfType<PaymentReleasedDomainEvent>().Single();

        raised.OrderId.Should().Be(payment.OrderId);
        raised.Amount.Should().Be(payment.Amount.Amount, "the amount let go is the amount that was held");
    }

    [Fact]
    public void Release_Should_BeANoOp_WhenTheMoneyHasAlreadyBeenTaken()
    {
        // Arrange — a cancellation racing an acceptance. This is the case that decides whether a
        // customer who cancelled a second too late gets their money back through a refund (§10) or
        // has the platform quietly claim the charge never happened.
        Payment payment = Authorized();
        payment.Capture(CapturedOn);
        payment.ClearDomainEvents();

        // Act
        Result result = payment.Release(CapturedOn.AddSeconds(5));

        // Assert
        result.IsSuccess.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Captured);
        payment.ReleasedOnUtc.Should().BeNull();
        payment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Capture_Should_BeANoOp_WhenTheHoldHasAlreadyBeenReleased()
    {
        // Arrange — the mirror: an acceptance arriving after a cancellation was acted on. Capturing
        // here would charge a card for an order that is not going to be made.
        Payment payment = Authorized();
        payment.Release(CapturedOn);
        payment.ClearDomainEvents();

        // Act
        Result result = payment.Capture(CapturedOn.AddSeconds(5));

        // Assert
        result.IsSuccess.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Released);
        payment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Release_Should_BeANoOp_WhenTheAuthorizationFailed()
    {
        // Arrange — nothing was ever held, so there is nothing to give up. Reachable when a customer
        // cancels an order whose card was refused at the same moment.
        Payment payment = NewPayment();
        payment.Fail(PaymentFailureReason.CardDeclined, null, AuthorizedOn);
        payment.ClearDomainEvents();

        // Act
        Result result = payment.Release(CapturedOn);

        // Assert
        result.IsSuccess.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Refund_Should_RefuseAPaymentThatWasNeverCaptured()
    {
        // Arrange — the hold is there, the money is not. Support's own ceiling passes this: it caps
        // the request at the replicated order subtotal, which says nothing about whether the charge
        // was ever taken. This is the check that can see the difference.
        Payment payment = Authorized();

        // Act
        Result result = payment.Refund(Money.Create(5m, "EUR").Value, alreadyRefunded: 0m, RefundedOn);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PaymentErrors.NotCaptured(payment.OrderId));
        payment.Status.Should().Be(PaymentStatus.Authorized);
    }

    [Fact]
    public void Refund_Should_RefuseMoreThanWasCaptured()
    {
        // Arrange
        Payment payment = Captured();

        // Act — one cent over the 24.99 that was taken.
        Result result = payment.Refund(Money.Create(25m, "EUR").Value, alreadyRefunded: 0m, RefundedOn);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PaymentErrors.RefundExceedsCaptured(payment.OrderId));
    }

    [Fact]
    public void Refund_Should_CountWhatHasAlreadyGoneBack()
    {
        // Arrange — 20 of the 24.99 is already refunded, so only 4.99 is left. This is the case a
        // ceiling against the order subtotal alone cannot catch at all: each request on its own is
        // well under it, and together they are not.
        Payment payment = Captured();

        // Act
        Result result = payment.Refund(Money.Create(5m, "EUR").Value, alreadyRefunded: 20m, RefundedOn);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PaymentErrors.RefundExceedsCaptured(payment.OrderId));
    }

    [Fact]
    public void Refund_Should_LeaveAPartlyRefundedPaymentCaptured()
    {
        // Arrange
        Payment payment = Captured();

        // Act
        Result result = payment.Refund(Money.Create(4m, "EUR").Value, alreadyRefunded: 0m, RefundedOn);

        // Assert — Captured, not Refunded: the status is about where the money is, and most of it is
        // still with the business. Marking it Refunded would also close the door on the next one.
        result.IsSuccess.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Captured);
        payment.RefundedOnUtc.Should().BeNull();
    }

    [Fact]
    public void Refund_Should_MarkThePaymentRefunded_WhenTheLastOfItGoesBack()
    {
        // Arrange — 20 already back, 4.99 left.
        Payment payment = Captured();

        // Act
        Result result = payment.Refund(Money.Create(4.99m, "EUR").Value, alreadyRefunded: 20m, RefundedOn);

        // Assert
        result.IsSuccess.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Refunded);
        payment.RefundedOnUtc.Should().Be(RefundedOn);
        payment.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void Refund_Should_RaiseNothing()
    {
        // Arrange
        Payment payment = Captured();

        // Act
        payment.Refund(Money.Create(24.99m, "EUR").Value, alreadyRefunded: 0m, RefundedOn);

        // Assert — the refund is its own aggregate and the event belongs to it. A second event about
        // the same money from here would give Support and Notifications two things to react to.
        payment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Refund_Should_RefuseAFullyRefundedPayment()
    {
        // Arrange — the whole amount is back, so the payment is terminal in the one state that is
        // still reachable by a refund. A further request has nothing left to draw on.
        Payment payment = Captured();
        payment.Refund(Money.Create(24.99m, "EUR").Value, alreadyRefunded: 0m, RefundedOn);

        // Act
        Result result = payment.Refund(Money.Create(0.01m, "EUR").Value, alreadyRefunded: 24.99m, RefundedOn);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PaymentErrors.RefundExceedsCaptured(payment.OrderId));
    }

    [Fact]
    public void Capture_Should_BeANoOp_OnARefundedPayment()
    {
        // Arrange — a late redelivery of the acceptance, long after the order was refunded.
        Payment payment = Captured();
        payment.Refund(Money.Create(24.99m, "EUR").Value, alreadyRefunded: 0m, RefundedOn);
        payment.ClearDomainEvents();

        // Act
        Result result = payment.Capture(RefundedOn);

        // Assert
        result.IsSuccess.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Refunded);
        payment.DomainEvents.Should().BeEmpty();
    }

    private static Payment Captured()
    {
        Payment payment = Authorized();

        payment.Capture(CapturedOn);
        payment.ClearDomainEvents();

        return payment;
    }

    private static Payment Authorized()
    {
        Payment payment = NewPayment();

        payment.Authorize(PaymentIntentId, AuthorizedOn);
        payment.ClearDomainEvents();

        return payment;
    }

    private static Payment NewPayment() => Payment.Start(
        Guid.CreateVersion7(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Money.Create(24.99m, "EUR").Value,
        CreatedOn).Value;
}
