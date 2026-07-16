using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Domain.Orders;
using FoodDeliveryService.Modules.Orders.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.Orders.UnitTests.Orders;

public class OrdersTests : BaseTest
{
    // ---- Place -------------------------------------------------------------

    [Fact]
    public void Place_ShouldRaiseOrderPlacedDomainEvent_WhenOrderIsPlaced()
    {
        // Act
        Order order = PlaceOrder(out Guid customerId, out Guid restaurantId);

        // Assert
        OrderPlacedDomainEvent domainEvent = AssertDomainEventWasPublished<OrderPlacedDomainEvent>(order);
        domainEvent.OrderId.Should().Be(order.Id);
        domainEvent.CustomerId.Should().Be(customerId);
        domainEvent.RestaurantId.Should().Be(restaurantId);
        domainEvent.Subtotal.Should().Be(order.Subtotal);
    }

    [Fact]
    public void Place_ShouldStartInPendingStatus()
    {
        // Act
        Order order = PlaceOrder();

        // Assert
        order.Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public void Place_ShouldComputeSubtotalFromServerPricedLines()
    {
        // Arrange
        OrderLine[] lines =
        [
            new(Guid.NewGuid(), "Margherita", 10.00m, 2),   // 20.00
            new(Guid.NewGuid(), "Tiramisu", 5.50m, 3)       // 16.50
        ];

        // Act
        Order order = Order.Place(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Address(),
            PaymentMethod.CashOnDelivery,
            lines,
            0.15m,
            Faker.Random.Guid().ToString(),
            DateTime.UtcNow).Value;

        // Assert
        order.Subtotal.Should().Be(36.50m);
        order.Items.Should().HaveCount(2);
        order.Items.Sum(item => item.LineTotal).Should().Be(36.50m);
    }

    [Fact]
    public void Place_ShouldReturnFailure_WhenThereAreNoLines()
    {
        // Act
        Result<Order> result = Order.Place(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Address(),
            PaymentMethod.CashOnDelivery,
            [],
            0.15m,
            Faker.Random.Guid().ToString(),
            DateTime.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.Empty);
    }

    // ---- Accept ------------------------------------------------------------

    [Fact]
    public void Accept_ShouldTransitionToAcceptedAndRaiseEvent_WhenPending()
    {
        // Arrange
        Order order = PlaceOrder(out Guid customerId, out Guid restaurantId);
        var acceptedOnUtc = DateTime.UtcNow;

        // Act
        Result result = order.Accept(acceptedOnUtc);

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Accepted);

        OrderAcceptedDomainEvent domainEvent = AssertDomainEventWasPublished<OrderAcceptedDomainEvent>(order);
        domainEvent.OrderId.Should().Be(order.Id);
        domainEvent.CustomerId.Should().Be(customerId);
        domainEvent.RestaurantId.Should().Be(restaurantId);
        domainEvent.AcceptedOnUtc.Should().Be(acceptedOnUtc);
    }

    [Fact]
    public void Accept_ShouldReturnFailure_WhenNotPending()
    {
        // Arrange
        Order order = PlaceOrder();
        order.Accept(DateTime.UtcNow);

        // Act — accepting an already-accepted order is illegal
        Result result = order.Accept(DateTime.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.InvalidTransition(OrderStatus.Accepted, OrderStatus.Accepted));
    }

    // ---- Reject ------------------------------------------------------------

    [Fact]
    public void Reject_ShouldTransitionToRejectedAndRaiseEvent_WhenPending()
    {
        // Arrange
        Order order = PlaceOrder(out Guid customerId, out Guid restaurantId);
        var reason = Faker.Lorem.Sentence();
        var rejectedOnUtc = DateTime.UtcNow;

        // Act
        Result result = order.Reject(reason, rejectedOnUtc);

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Rejected);

        OrderRejectedDomainEvent domainEvent = AssertDomainEventWasPublished<OrderRejectedDomainEvent>(order);
        domainEvent.OrderId.Should().Be(order.Id);
        domainEvent.CustomerId.Should().Be(customerId);
        domainEvent.RestaurantId.Should().Be(restaurantId);
        domainEvent.Reason.Should().Be(reason);
        domainEvent.RejectedOnUtc.Should().Be(rejectedOnUtc);
    }

    [Fact]
    public void Reject_ShouldReturnFailure_WhenNotPending()
    {
        // Arrange
        Order order = PlaceOrder();
        order.Accept(DateTime.UtcNow);

        // Act
        Result result = order.Reject(Faker.Lorem.Sentence(), DateTime.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.InvalidTransition(OrderStatus.Accepted, OrderStatus.Rejected));
    }

    // ---- StartPreparing ----------------------------------------------------

    [Fact]
    public void StartPreparing_ShouldTransitionToPreparingAndRaiseEvent_WhenAccepted()
    {
        // Arrange
        Order order = PlaceOrder(out Guid customerId, out Guid restaurantId);
        order.Accept(DateTime.UtcNow);

        // Act
        Result result = order.StartPreparing();

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Preparing);

        OrderPreparingDomainEvent domainEvent = AssertDomainEventWasPublished<OrderPreparingDomainEvent>(order);
        domainEvent.OrderId.Should().Be(order.Id);
        domainEvent.CustomerId.Should().Be(customerId);
        domainEvent.RestaurantId.Should().Be(restaurantId);
    }

    [Fact]
    public void StartPreparing_ShouldReturnFailureAndNotRaiseEvent_WhenNotAccepted()
    {
        // Arrange — still Pending, never accepted
        Order order = PlaceOrder();

        // Act
        Result result = order.StartPreparing();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.InvalidTransition(OrderStatus.Pending, OrderStatus.Preparing));
        order.DomainEvents.OfType<OrderPreparingDomainEvent>().Should().BeEmpty();
    }

    // ---- MarkReadyForPickup ------------------------------------------------

    [Fact]
    public void MarkReadyForPickup_ShouldTransitionAndRaiseEvent_WhenPreparing()
    {
        // Arrange
        Order order = PlaceOrder(out Guid customerId, out Guid restaurantId);
        order.Accept(DateTime.UtcNow);
        order.StartPreparing();

        // Act
        Result result = order.MarkReadyForPickup();

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.ReadyForPickup);

        OrderReadyForPickupDomainEvent domainEvent =
            AssertDomainEventWasPublished<OrderReadyForPickupDomainEvent>(order);
        domainEvent.OrderId.Should().Be(order.Id);
        domainEvent.CustomerId.Should().Be(customerId);
        domainEvent.RestaurantId.Should().Be(restaurantId);
    }

    [Fact]
    public void MarkReadyForPickup_ShouldReturnFailure_WhenNotPreparing()
    {
        // Arrange — Accepted but preparation never started
        Order order = PlaceOrder();
        order.Accept(DateTime.UtcNow);

        // Act
        Result result = order.MarkReadyForPickup();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.InvalidTransition(OrderStatus.Accepted, OrderStatus.ReadyForPickup));
    }

    // ---- Cancel ------------------------------------------------------------

    [Fact]
    public void Cancel_ShouldTransitionToCancelledAndRaiseEvent_WhenPending()
    {
        // Arrange
        Order order = PlaceOrder(out Guid customerId, out Guid restaurantId);
        var cancelledOnUtc = DateTime.UtcNow;

        // Act
        Result result = order.Cancel(cancelledOnUtc);

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);

        OrderCancelledDomainEvent domainEvent = AssertDomainEventWasPublished<OrderCancelledDomainEvent>(order);
        domainEvent.OrderId.Should().Be(order.Id);
        domainEvent.CustomerId.Should().Be(customerId);
        domainEvent.RestaurantId.Should().Be(restaurantId);
        domainEvent.CancelledOnUtc.Should().Be(cancelledOnUtc);
    }

    [Fact]
    public void Cancel_ShouldSucceed_WhenAccepted()
    {
        // Arrange
        Order order = PlaceOrder();
        order.Accept(DateTime.UtcNow);

        // Act
        Result result = order.Cancel(DateTime.UtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void Cancel_ShouldReturnFailure_WhenAlreadyPreparing()
    {
        // Arrange — once the kitchen is cooking, the customer can no longer back out
        Order order = PlaceOrder();
        order.Accept(DateTime.UtcNow);
        order.StartPreparing();

        // Act
        Result result = order.Cancel(DateTime.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.InvalidTransition(OrderStatus.Preparing, OrderStatus.Cancelled));
    }

    // ---- MarkOutForDelivery / MarkDelivered (Delivery-driven, modeled here) --

    [Fact]
    public void MarkOutForDelivery_ShouldTransitionAndRaiseEvent_WhenReadyForPickup()
    {
        // Arrange
        Order order = ReadyForPickupOrder();

        // Act
        Result result = order.MarkOutForDelivery();

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.OutForDelivery);
        AssertDomainEventWasPublished<OrderOutForDeliveryDomainEvent>(order);
    }

    [Fact]
    public void MarkOutForDelivery_ShouldReturnFailure_WhenNotReadyForPickup()
    {
        // Arrange — still Preparing
        Order order = PlaceOrder();
        order.Accept(DateTime.UtcNow);
        order.StartPreparing();

        // Act
        Result result = order.MarkOutForDelivery();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.InvalidTransition(OrderStatus.Preparing, OrderStatus.OutForDelivery));
    }

    [Fact]
    public void MarkDelivered_ShouldTransitionAndRaiseEvent_WhenOutForDelivery()
    {
        // Arrange
        Order order = ReadyForPickupOrder();
        order.MarkOutForDelivery();
        var deliveredOnUtc = DateTime.UtcNow;

        // Act
        Result result = order.MarkDelivered(deliveredOnUtc);

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Delivered);

        OrderDeliveredDomainEvent domainEvent = AssertDomainEventWasPublished<OrderDeliveredDomainEvent>(order);
        domainEvent.OrderId.Should().Be(order.Id);
        domainEvent.DeliveredOnUtc.Should().Be(deliveredOnUtc);
    }

    [Fact]
    public void MarkDelivered_ShouldReturnFailure_WhenNotOutForDelivery()
    {
        // Arrange — ReadyForPickup, delivery not yet dispatched
        Order order = ReadyForPickupOrder();

        // Act
        Result result = order.MarkDelivered(DateTime.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.InvalidTransition(OrderStatus.ReadyForPickup, OrderStatus.Delivered));
    }

    // ---- Helpers -----------------------------------------------------------

    private static Order PlaceOrder() => PlaceOrder(out _, out _);

    private static Order PlaceOrder(out Guid customerId, out Guid restaurantId)
    {
        customerId = Guid.NewGuid();
        restaurantId = Guid.NewGuid();

        OrderLine[] lines = [new(Guid.NewGuid(), Faker.Commerce.ProductName(), 9.99m, 2)];

        return Order.Place(
            customerId,
            restaurantId,
            Address(),
            PaymentMethod.CashOnDelivery,
            lines,
            0.15m,
            Faker.Random.Guid().ToString(),
            DateTime.UtcNow).Value;
    }

    private static Order ReadyForPickupOrder()
    {
        Order order = PlaceOrder();
        order.Accept(DateTime.UtcNow);
        order.StartPreparing();
        order.MarkReadyForPickup();
        return order;
    }

    private static DeliveryAddress Address() =>
        new(
            Faker.Address.StreetAddress(),
            Faker.Address.City(),
            Faker.Address.ZipCode(),
            Faker.Address.Country(),
            Notes: null);
}
