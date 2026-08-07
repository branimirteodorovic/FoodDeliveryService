using AwesomeAssertions;
using FoodDeliveryService.Modules.FraudDetection.Domain.Customers;
using FoodDeliveryService.Modules.FraudDetection.Domain.Orders;
using FoodDeliveryService.Modules.FraudDetection.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using FoodDeliveryService.Modules.Users.IntegrationEvents;

namespace FoodDeliveryService.Modules.FraudDetection.IntegrationTests.Projections;

public class CustomerBehaviourProjectionTests(IntegrationTestWebAppFactory factory)
    : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task UserRegistered_Should_RecordTheAccountAge()
    {
        // Arrange
        var userId = Guid.NewGuid();
        DateTime registeredOnUtc = DateTime.UtcNow.AddDays(-2);

        // Act
        await Factory.PublishAsync(
            new UserRegisteredIntegrationEvent(
                Guid.NewGuid(),
                registeredOnUtc,
                userId,
                Faker.Internet.Email(),
                Faker.Name.FirstName(),
                Faker.Name.LastName(),
                CustomerRoles),
            TestContext.Current.CancellationToken);

        // Assert
        CustomerBehaviour behaviour = await WaitForCustomerAsync(
            userId,
            c => c.RegisteredOnUtc is not null,
            "the registration event should populate the account age",
            TestContext.Current.CancellationToken);

        behaviour.RegisteredOnUtc.Should().BeCloseTo(registeredOnUtc, TimeSpan.FromSeconds(1));
        behaviour.OrdersPlaced.Should().Be(0);
    }

    [Fact]
    public async Task OrderPlaced_Should_CreateTheOrderFact_And_CountTheOrder()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        DateTime placedOnUtc = DateTime.UtcNow;

        // Act — no registration first: the customer row must be created by whichever event arrives
        // first, which is exactly the ordering guarantee FraudDetection does NOT have across services.
        await Factory.PublishAsync(
            new OrderPlacedIntegrationEvent(
                Guid.NewGuid(),
                placedOnUtc,
                orderId,
                customerId,
                restaurantId,
                31.20m,
                placedOnUtc),
            TestContext.Current.CancellationToken);

        // Assert
        CustomerBehaviour behaviour = await WaitForCustomerAsync(
            customerId,
            c => c.OrdersPlaced == 1,
            "the placed order should be counted on a projection row created on demand",
            TestContext.Current.CancellationToken);

        behaviour.RegisteredOnUtc.Should().BeNull("no registration event was published for this customer");
        behaviour.TotalOrderValue.Should().Be(31.20m);
        behaviour.OrdersPlacedInWindow.Should().Be(1);

        OrderFact fact = await WaitForOrderFactAsync(
            orderId,
            f => f.Status == OrderFactStatus.Placed,
            "the fact row is what every later signal reads",
            TestContext.Current.CancellationToken);

        fact.CustomerId.Should().Be(customerId);
        fact.RestaurantId.Should().Be(restaurantId);
        fact.Subtotal.Should().Be(31.20m);
    }

    [Fact]
    public async Task CancellationAfterAcceptance_Should_ConvergeOnTheCancelBeforePickupCounter()
    {
        // Arrange — the promotion-abuse shape: accepted by the restaurant, cancelled before pickup.
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        DateTime placedOnUtc = DateTime.UtcNow;

        await Factory.PublishAsync(
            new OrderPlacedIntegrationEvent(
                Guid.NewGuid(), placedOnUtc, orderId, customerId, restaurantId, 45.00m, placedOnUtc),
            TestContext.Current.CancellationToken);

        // Wait for the fact before accepting: the two events reach the inbox on separate queues, and
        // the "cancelled before pickup" derivation depends on the acceptance having landed first.
        await WaitForOrderFactAsync(
            orderId,
            f => f.Status == OrderFactStatus.Placed,
            "the order must be projected before it can be accepted",
            TestContext.Current.CancellationToken);

        await Factory.PublishAsync(
            new OrderAcceptedIntegrationEvent(
                Guid.NewGuid(), placedOnUtc.AddMinutes(2), orderId, customerId, restaurantId,
                placedOnUtc.AddMinutes(2)),
            TestContext.Current.CancellationToken);

        await WaitForOrderFactAsync(
            orderId,
            f => f.Status == OrderFactStatus.Accepted,
            "the acceptance should advance the fact",
            TestContext.Current.CancellationToken);

        // Act
        await Factory.PublishAsync(
            new OrderCancelledIntegrationEvent(
                Guid.NewGuid(), placedOnUtc.AddMinutes(5), orderId, customerId, restaurantId,
                placedOnUtc.AddMinutes(5)),
            TestContext.Current.CancellationToken);

        // Assert
        CustomerBehaviour behaviour = await WaitForCustomerAsync(
            customerId,
            c => c.OrdersCancelled == 1,
            "the cancellation should be counted",
            TestContext.Current.CancellationToken);

        behaviour.OrdersPlaced.Should().Be(1);
        behaviour.CancelledBeforePickup.Should().Be(
            1,
            "the order had been accepted but never picked up — the shape the Milestone B signal looks for");

        OrderFact fact = await WaitForOrderFactAsync(
            orderId,
            f => f.Status == OrderFactStatus.Cancelled,
            "the fact should close as cancelled",
            TestContext.Current.CancellationToken);

        fact.CancelledBeforePickup.Should().BeTrue();
    }

    [Fact]
    public async Task OrderRejected_Should_CountSeparatelyFromCancellations()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        DateTime placedOnUtc = DateTime.UtcNow;

        await Factory.PublishAsync(
            new OrderPlacedIntegrationEvent(
                Guid.NewGuid(), placedOnUtc, orderId, customerId, restaurantId, 12.00m, placedOnUtc),
            TestContext.Current.CancellationToken);

        await WaitForOrderFactAsync(
            orderId,
            f => f.Status == OrderFactStatus.Placed,
            "the order must be projected before it can be rejected",
            TestContext.Current.CancellationToken);

        // Act
        await Factory.PublishAsync(
            new OrderRejectedIntegrationEvent(
                Guid.NewGuid(), placedOnUtc.AddMinutes(1), orderId, customerId, restaurantId,
                "Out of stock", placedOnUtc.AddMinutes(1)),
            TestContext.Current.CancellationToken);

        // Assert
        CustomerBehaviour behaviour = await WaitForCustomerAsync(
            customerId,
            c => c.OrdersRejected == 1,
            "the rejection should be counted",
            TestContext.Current.CancellationToken);

        behaviour.OrdersCancelled.Should().Be(0, "a restaurant refusing is not the customer cancelling");
    }
}
