using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Domain;
using FoodDeliveryService.Modules.Payments.Domain.Refunds;

namespace FoodDeliveryService.Modules.Payments.UnitTests;

/// <summary>
/// The refund state machine — Feature 3.8 Milestone H, §10.1.
/// <para>
/// Shorter than <c>PaymentTests</c> because a refund has one writer rather than two: nothing
/// reconciles it from a webhook, so the redelivery it has to survive is the inbox's own. What the
/// cases below are mostly about is that <em>every</em> ending is published — a refund that cannot
/// happen is an answer an agent is waiting on, and silence there is the failure mode this milestone
/// exists to remove rather than one it may introduce at the far end.
/// </para>
/// </summary>
public class RefundTests
{
    private static readonly DateTime CreatedOn = new(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SettledOn = new(2026, 9, 16, 10, 0, 2, DateTimeKind.Utc);

    private const string ProviderRefundId = "re_test_refund";
    private const string TicketReference = "SUP-00001234";

    [Fact]
    public void Start_Should_RecordTheIntentToRefund_WithoutAnnouncingAnything()
    {
        // Act
        Refund refund = NewRefund();

        // Assert — the row exists before any provider call, which is what makes a lost response
        // recoverable rather than money out of the business with nothing pointing at it.
        refund.Status.Should().Be(RefundStatus.Pending);
        refund.StripeRefundId.Should().BeNull();
        refund.CreatedOnUtc.Should().Be(CreatedOn);
        refund.DomainEvents.Should().BeEmpty(
            "the approval Support published is the news; a refund nobody has attempted has no outcome yet");
    }

    [Fact]
    public void Start_Should_RefuseAZeroAmount()
    {
        // Arrange — Money allows zero, so this bound belongs to the aggregate that spends it.
        Money zero = Money.Create(0m, "EUR").Value;

        // Act
        Result<Refund> result = Refund.Start(
            Guid.CreateVersion7(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            TicketReference,
            Guid.NewGuid(),
            Guid.NewGuid(),
            zero,
            CreatedOn);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RefundErrors.AmountNotPositive);
    }

    [Fact]
    public void Start_Should_RefuseAMissingTicketReference()
    {
        // Act
        Result<Refund> result = Refund.Start(
            Guid.CreateVersion7(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "   ",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Money.Create(5m, "EUR").Value,
            CreatedOn);

        // Assert — unreachable from Support, and checked because the reference is the one field in
        // this aggregate a customer reads, in the subject line of an email.
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RefundErrors.TicketReferenceRequired);
    }

    [Fact]
    public void Settle_Should_RecordTheProviderRefund_AndPublishIt()
    {
        // Arrange
        Refund refund = NewRefund();

        // Act
        Result result = refund.Settle(ProviderRefundId, SettledOn);

        // Assert
        result.IsSuccess.Should().BeTrue();
        refund.Status.Should().Be(RefundStatus.Settled);
        refund.StripeRefundId.Should().Be(ProviderRefundId);
        refund.SettledOnUtc.Should().Be(SettledOn);
        refund.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<RefundSettledDomainEvent>();
    }

    [Fact]
    public void Settle_Should_CarryTheTicketReference_SoTheEmailCanQuoteIt()
    {
        // Arrange
        Refund refund = NewRefund();

        // Act
        refund.Settle(ProviderRefundId, SettledOn);

        // Assert — Notifications may not ask Support for it (hard rule #5), so if it is not on the
        // event the customer's email has nothing to name the case by.
        var settled = (RefundSettledDomainEvent)refund.DomainEvents.Single();
        settled.TicketReference.Should().Be(TicketReference);
        settled.Amount.Should().Be(12.50m);
        settled.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Settle_Should_BeANoOp_WhenAlreadySettled()
    {
        // Arrange — a redelivered approval that somehow got past the repository lookup.
        Refund refund = NewRefund();
        refund.Settle(ProviderRefundId, SettledOn);
        refund.ClearDomainEvents();

        // Act
        Result result = refund.Settle(ProviderRefundId, SettledOn);

        // Assert — no second event, so Support does not record a second settlement and the customer
        // is not emailed twice about one refund.
        result.IsSuccess.Should().BeTrue();
        refund.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Settle_Should_RefuseAnEmptyProviderIdentifier()
    {
        // Arrange
        Refund refund = NewRefund();

        // Act
        Result result = refund.Settle("  ", SettledOn);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RefundErrors.ProviderRefundRequired);
        refund.Status.Should().Be(RefundStatus.Pending);
    }

    [Theory]
    [InlineData(RefundFailureReason.NotCardPayment)]
    [InlineData(RefundFailureReason.PaymentNotCaptured)]
    [InlineData(RefundFailureReason.AmountExceedsCaptured)]
    [InlineData(RefundFailureReason.GatewayError)]
    public void Fail_Should_PublishEveryRefusal(string reason)
    {
        // Arrange
        Refund refund = NewRefund();

        // Act
        Result result = refund.Fail(reason, SettledOn);

        // Assert — including the three that never reached the provider. Support is waiting on an
        // answer either way, and an approved request that goes quiet is the worst of the outcomes.
        result.IsSuccess.Should().BeTrue();
        refund.Status.Should().Be(RefundStatus.Failed);
        refund.FailureReason.Should().Be(reason);
        refund.FailedOnUtc.Should().Be(SettledOn);

        var failed = (RefundFailedDomainEvent)refund.DomainEvents.Single();
        failed.Reason.Should().Be(reason);
    }

    [Fact]
    public void Fail_Should_RefuseAReasonOutsideTheBoundedSet()
    {
        // Arrange — the value reaches a metric tag and Support's audit log, so an unbounded string
        // here is a cardinality problem in one place and an unreadable log entry in the other.
        Refund refund = NewRefund();

        // Act
        Result result = refund.Fail("Your card was declined. Try another card.", SettledOn);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Refunds.FailureReasonUnknown");
        refund.Status.Should().Be(RefundStatus.Pending);
    }

    [Fact]
    public void Fail_Should_BeANoOp_OnASettledRefund()
    {
        // Arrange
        Refund refund = NewRefund();
        refund.Settle(ProviderRefundId, SettledOn);
        refund.ClearDomainEvents();

        // Act
        Result result = refund.Fail(RefundFailureReason.GatewayError, SettledOn);

        // Assert — money that has gone back does not un-go back because a later message says so.
        result.IsSuccess.Should().BeTrue();
        refund.Status.Should().Be(RefundStatus.Settled);
        refund.DomainEvents.Should().BeEmpty();
    }

    private static Refund NewRefund() => Refund.Start(
        Guid.CreateVersion7(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        TicketReference,
        Guid.NewGuid(),
        Guid.NewGuid(),
        Money.Create(12.50m, "EUR").Value,
        CreatedOn).Value;
}
