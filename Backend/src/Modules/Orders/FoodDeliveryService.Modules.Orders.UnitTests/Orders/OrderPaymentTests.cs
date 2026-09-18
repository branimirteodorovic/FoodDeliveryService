using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Domain.Orders;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using FoodDeliveryService.Modules.Orders.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.Orders.UnitTests.Orders;

/// <summary>
/// The order's payment dimension — Feature 3.8 Milestone F (§8.2, §8.3) and Milestone G (§9).
/// <para>
/// Every case here has a cash counterpart, and the counterparts are the point: the whole argument
/// for a second column instead of a ninth <c>OrderStatus</c> member (§1.2) is that the two
/// dimensions are independent. If a cash order could be refused an acceptance, or could be cancelled
/// by a payment event, that independence would be a claim rather than a fact.
/// </para>
/// </summary>
public class OrderPaymentTests : BaseTest
{
    private const string Reason = "card_declined";

    [Fact]
    public void Place_Should_PutACardOrderInAuthorizing()
    {
        // Act
        Order order = PlaceOrder(PaymentMethod.Card);

        // Assert — a card order is placed already waiting on its hold.
        order.Status.Should().Be(OrderStatus.Pending);
        order.PaymentStatus.Should().Be(PaymentStatus.Authorizing);
    }

    [Fact]
    public void Place_Should_PutACashOrderInNotRequired()
    {
        // Act
        Order order = PlaceOrder(PaymentMethod.CashOnDelivery);

        // Assert — and it stays there for the whole life of the order.
        order.PaymentStatus.Should().Be(PaymentStatus.NotRequired);
    }

    [Fact]
    public void PlacedEvent_Should_CarryThePaymentMethod()
    {
        // Arrange — it is what lets Payments skip cash orders without asking Orders anything.
        Order order = PlaceOrder(PaymentMethod.Card);

        // Act
        OrderPlacedDomainEvent raised = AssertDomainEventWasPublished<OrderPlacedDomainEvent>(order);

        // Assert
        raised.PaymentMethod.Should().Be(PaymentMethod.Card);
    }

    [Fact]
    public void PaymentMethodNames_Should_MatchTheContractVocabulary()
    {
        // The integration event carries the payment method as a string, because hard rule #4 puts
        // this module's Domain out of Payments' reach. That makes the enum's member NAMES part of a
        // cross-service contract — rename one without renaming the constant beside it and every card
        // order silently stops being charged, with nothing failing anywhere.
        PaymentMethod.Card.ToString().Should().Be(OrderPaymentMethods.Card);
        PaymentMethod.CashOnDelivery.ToString().Should().Be(OrderPaymentMethods.CashOnDelivery);
    }

    [Fact]
    public void Accept_Should_BeRefused_WhileACardOrderIsStillAuthorizing()
    {
        // Arrange — the restaurant reaching for the order inside the authorization window.
        Order order = PlaceOrder(PaymentMethod.Card);

        // Act
        Result result = order.Accept(DateTime.UtcNow);

        // Assert — a clean, retryable error rather than an invalid transition: the order is in a
        // perfectly good state, the money just is not there yet.
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.PaymentNotAuthorized);
        order.Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public void Accept_Should_Succeed_OnceTheCardOrderIsAuthorized()
    {
        // Arrange
        Order order = PlaceOrder(PaymentMethod.Card);
        order.MarkPaymentAuthorized();

        // Act
        Result result = order.Accept(DateTime.UtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Accepted);
    }

    [Fact]
    public void Accept_Should_Succeed_ForACashOrder_WithNoPaymentAnywhere()
    {
        // Arrange — the counterpart. Nothing about the payment dimension may reach a cash order.
        Order order = PlaceOrder(PaymentMethod.CashOnDelivery);

        // Act
        Result result = order.Accept(DateTime.UtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.PaymentStatus.Should().Be(PaymentStatus.NotRequired);
    }

    [Fact]
    public void MarkPaymentAuthorized_Should_BeANoOp_WhenAppliedTwice()
    {
        // Arrange — the inbox dispatches at least once.
        Order order = PlaceOrder(PaymentMethod.Card);
        order.MarkPaymentAuthorized();
        order.ClearDomainEvents();

        // Act
        Result result = order.MarkPaymentAuthorized();

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.PaymentStatus.Should().Be(PaymentStatus.Authorized);
        order.DomainEvents.Should().BeEmpty("this is a projection, and a projection announces nothing");
    }

    [Fact]
    public void MarkPaymentAuthorized_Should_BeRefused_ForACashOrder()
    {
        // Act
        Result result = PlaceOrder(PaymentMethod.CashOnDelivery).MarkPaymentAuthorized();

        // Assert — reachable only from a misrouted event, and a failure rather than a silent no-op
        // because that is a bug somewhere that somebody should see on the inbox row.
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.PaymentNotRequired);
    }

    [Fact]
    public void FailPayment_Should_CancelTheOrder_AndRaiseItsOwnEvent()
    {
        // Arrange
        Order order = PlaceOrder(PaymentMethod.Card);
        var failedOn = new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc);

        // Act
        Result result = order.FailPayment(Reason, failedOn);

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.PaymentStatus.Should().Be(PaymentStatus.Failed);

        OrderPaymentFailedDomainEvent raised =
            AssertDomainEventWasPublished<OrderPaymentFailedDomainEvent>(order);

        raised.Reason.Should().Be(Reason);
        raised.PreviousStatus.Should().Be(OrderStatus.Pending);
        raised.FailedOnUtc.Should().Be(failedOn);
    }

    [Fact]
    public void FailPayment_Should_NotRaiseACancellation()
    {
        // Arrange — §8.3, and the reason the distinct event exists at all. Payments consumes
        // OrderCancelled in order to RELEASE an authorization; raising it here would ask Payments to
        // release the hold of a payment that just failed to be held.
        Order order = PlaceOrder(PaymentMethod.Card);

        // Act
        order.FailPayment(Reason, DateTime.UtcNow);

        // Assert
        order.DomainEvents.OfType<OrderCancelledDomainEvent>().Should().BeEmpty();
    }

    [Fact]
    public void FailPayment_Should_BeANoOp_WhenTheSameFailureArrivesTwice()
    {
        // Arrange
        Order order = PlaceOrder(PaymentMethod.Card);
        order.FailPayment(Reason, DateTime.UtcNow);
        order.ClearDomainEvents();

        // Act
        Result result = order.FailPayment(Reason, DateTime.UtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void FailPayment_Should_RecordTheOutcome_WhenTheCustomerAlreadyCancelled()
    {
        // Arrange — the customer backed out while the authorization was still in flight. The order
        // is already where a failed payment would put it, so there is nothing to cancel and nobody
        // to tell; the payment outcome is still worth recording.
        Order order = PlaceOrder(PaymentMethod.Card);
        order.Cancel(DateTime.UtcNow);
        order.ClearDomainEvents();

        // Act
        Result result = order.FailPayment(Reason, DateTime.UtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.PaymentStatus.Should().Be(PaymentStatus.Failed);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void FailPayment_Should_BeRefused_ForACashOrder()
    {
        // Arrange — the counterpart that matters most: a misrouted payment failure must never
        // cancel an order that was never going to be charged.
        Order order = PlaceOrder(PaymentMethod.CashOnDelivery);

        // Act
        Result result = order.FailPayment(Reason, DateTime.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.PaymentNotRequired);
        order.Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public void MarkPaymentCaptured_Should_RecordTheCharge_WithoutTouchingTheLifecycle()
    {
        // Arrange — Feature 3.8 Milestone G. The restaurant accepted first; the capture is the
        // money catching up with a decision the order already made.
        Order order = Accepted();

        // Act
        Result result = order.MarkPaymentCaptured();

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.PaymentStatus.Should().Be(PaymentStatus.Captured);
        order.Status.Should().Be(OrderStatus.Accepted, "the money dimension never moves the lifecycle");
        order.DomainEvents.Should().BeEmpty("this is a projection of Payments' state, not news of this module's");
    }

    [Fact]
    public void MarkPaymentCaptured_Should_BeANoOp_WhenTheHoldWasAlreadyReleased()
    {
        // Arrange — the inbox is unordered and at-least-once, so a capture can arrive after a
        // release was projected. Overwriting it would tell a customer they were charged for an
        // order that ended without a charge.
        Order order = Accepted();
        order.MarkPaymentReleased();

        // Act
        Result result = order.MarkPaymentCaptured();

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.PaymentStatus.Should().Be(PaymentStatus.Released);
    }

    [Fact]
    public void MarkPaymentReleased_Should_RecordThatNothingWasCharged()
    {
        // Arrange — the restaurant rejected the order after the hold was placed.
        Order order = PlaceOrder(PaymentMethod.Card);
        order.MarkPaymentAuthorized();
        order.Reject("The kitchen is closed", DateTime.UtcNow);
        order.ClearDomainEvents();

        // Act
        Result result = order.MarkPaymentReleased();

        // Assert — Released, not Failed: a released hold is an order that ended, a failed payment is
        // a card that was refused, and "was I charged?" has a different answer in each.
        result.IsSuccess.Should().BeTrue();
        order.PaymentStatus.Should().Be(PaymentStatus.Released);
        order.Status.Should().Be(OrderStatus.Rejected);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void MarkPaymentReleased_Should_BeANoOp_WhenTheMoneyWasAlreadyTaken()
    {
        // Arrange — a cancellation racing an acceptance. Payments made the same call on its own
        // aggregate; this is the projection agreeing with it.
        Order order = Accepted();
        order.MarkPaymentCaptured();

        // Act
        Result result = order.MarkPaymentReleased();

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.PaymentStatus.Should().Be(PaymentStatus.Captured);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PaymentProjections_Should_BeRefused_ForACashOrder(bool capture)
    {
        // Arrange — the cash counterpart, the one that keeps the two dimensions independent (§1.2).
        // A misrouted capture must never claim a cash order was charged to a card.
        Order order = PlaceOrder(PaymentMethod.CashOnDelivery);

        // Act
        Result result = capture ? order.MarkPaymentCaptured() : order.MarkPaymentReleased();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.PaymentNotRequired);
        order.PaymentStatus.Should().Be(PaymentStatus.NotRequired);
    }

    private static Order Accepted()
    {
        Order order = PlaceOrder(PaymentMethod.Card);

        order.MarkPaymentAuthorized();
        order.Accept(DateTime.UtcNow);
        order.ClearDomainEvents();

        return order;
    }

    private static Order PlaceOrder(PaymentMethod paymentMethod)
    {
        OrderLine[] lines = [new(Guid.NewGuid(), Faker.Commerce.ProductName(), 12.50m, 2)];

        return Order.Place(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DeliveryAddress(
                Faker.Address.StreetAddress(),
                Faker.Address.City(),
                Faker.Address.ZipCode(),
                Faker.Address.Country(),
                Notes: null,
                Faker.Address.Latitude(),
                Faker.Address.Longitude()),
            paymentMethod,
            lines,
            0.15m,
            Faker.Random.Guid().ToString(),
            DateTime.UtcNow).Value;
    }
}
