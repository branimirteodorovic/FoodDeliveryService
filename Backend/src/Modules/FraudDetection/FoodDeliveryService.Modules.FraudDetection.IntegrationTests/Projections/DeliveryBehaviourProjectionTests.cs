using AwesomeAssertions;
using FoodDeliveryService.Modules.Delivery.IntegrationEvents;
using FoodDeliveryService.Modules.FraudDetection.Domain.Customers;
using FoodDeliveryService.Modules.FraudDetection.Domain.Drivers;
using FoodDeliveryService.Modules.FraudDetection.Domain.Orders;
using FoodDeliveryService.Modules.FraudDetection.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;

namespace FoodDeliveryService.Modules.FraudDetection.IntegrationTests.Projections;

public class DeliveryBehaviourProjectionTests(IntegrationTestWebAppFactory factory)
    : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task FullOrderLifecycle_Should_ConvergeAcrossAllThreeProjections()
    {
        // Arrange — the whole path, published by two different services, exactly as production does.
        var customerId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var deliveryId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        DateTime placedOnUtc = DateTime.UtcNow;

        await Factory.PublishAsync(
            new OrderPlacedIntegrationEvent(
                Guid.NewGuid(), placedOnUtc, orderId, customerId, restaurantId, 58.75m, placedOnUtc),
            TestContext.Current.CancellationToken);

        await WaitForOrderFactAsync(
            orderId,
            f => f.Status == OrderFactStatus.Placed,
            "the fact row anchors the rest of the lifecycle",
            TestContext.Current.CancellationToken);

        // The only shipped event carrying the delivery coordinates — the reason FraudDetection consumes it.
        await Factory.PublishAsync(
            new OrderReadyForPickupIntegrationEvent(
                Guid.NewGuid(),
                placedOnUtc.AddMinutes(15),
                orderId,
                customerId,
                restaurantId,
                44.8125,
                20.4612,
                Faker.Address.StreetAddress(),
                "Belgrade",
                "11000",
                "Serbia",
                null,
                44.7866,
                20.4489,
                58.75m,
                placedOnUtc),
            TestContext.Current.CancellationToken);

        OrderFact readyFact = await WaitForOrderFactAsync(
            orderId,
            f => f.DropoffLatitude is not null,
            "the drop-off coordinates should be captured for the Milestone D location check",
            TestContext.Current.CancellationToken);

        readyFact.Status.Should().Be(OrderFactStatus.ReadyForPickup);
        readyFact.DropoffLatitude.Should().BeApproximately(44.7866, 0.0001);
        readyFact.DropoffLongitude.Should().BeApproximately(20.4489, 0.0001);

        // Act — the Delivery half.
        await Factory.PublishAsync(
            new OrderPickedUpIntegrationEvent(
                Guid.NewGuid(), placedOnUtc.AddMinutes(20), orderId, deliveryId, driverId,
                placedOnUtc.AddMinutes(20)),
            TestContext.Current.CancellationToken);

        await WaitForOrderFactAsync(
            orderId,
            f => f.Status == OrderFactStatus.PickedUp,
            "the pickup should advance the fact and bind the driver",
            TestContext.Current.CancellationToken);

        await Factory.PublishAsync(
            new OrderDeliveredIntegrationEvent(
                Guid.NewGuid(), placedOnUtc.AddMinutes(45), orderId, deliveryId, driverId,
                placedOnUtc.AddMinutes(45)),
            TestContext.Current.CancellationToken);

        // Assert
        OrderFact fact = await WaitForOrderFactAsync(
            orderId,
            f => f.Status == OrderFactStatus.Delivered,
            "the fact should close as delivered",
            TestContext.Current.CancellationToken);

        fact.DriverId.Should().Be(driverId);
        fact.DeliveryId.Should().Be(deliveryId);
        fact.IsOpen.Should().BeFalse();

        DriverBehaviour driver = await WaitForDriverAsync(
            driverId,
            d => d.DeliveriesCompleted == 1,
            "the driver projection is created on demand by the delivery events",
            TestContext.Current.CancellationToken);

        driver.PickupsCompleted.Should().Be(1);

        CustomerBehaviour customer = await WaitForCustomerAsync(
            customerId,
            c => c.OrdersDelivered == 1,
            "the delivery event carries no customer — the fact row is what attributes it",
            TestContext.Current.CancellationToken);

        customer.OrdersPlaced.Should().Be(1);
        customer.OrdersCancelled.Should().Be(0);
    }

    [Fact]
    public async Task DeliveryOfferRejected_Should_CountAgainstTheDriver()
    {
        // Arrange
        var driverId = Guid.NewGuid();

        // Act
        await Factory.PublishAsync(
            new DeliveryOfferRejectedIntegrationEvent(
                Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), driverId),
            TestContext.Current.CancellationToken);

        // Assert
        DriverBehaviour driver = await WaitForDriverAsync(
            driverId,
            d => d.OffersRejected == 1,
            "a declined offer should be counted even for a driver FraudDetection has never seen deliver",
            TestContext.Current.CancellationToken);

        driver.DeliveriesCompleted.Should().Be(0);
    }

    [Fact]
    public async Task DeliveryUnassigned_Should_CountOnTheOrder_NotOnAnyDriver()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        DateTime placedOnUtc = DateTime.UtcNow;

        await Factory.PublishAsync(
            new OrderPlacedIntegrationEvent(
                Guid.NewGuid(), placedOnUtc, orderId, customerId, Guid.NewGuid(), 20.00m, placedOnUtc),
            TestContext.Current.CancellationToken);

        await WaitForOrderFactAsync(
            orderId,
            f => f.Status == OrderFactStatus.Placed,
            "the order must be projected before it can be left unassigned",
            TestContext.Current.CancellationToken);

        // Act — the event names no driver, because no driver took it.
        await Factory.PublishAsync(
            new DeliveryUnassignedIntegrationEvent(
                Guid.NewGuid(), placedOnUtc.AddMinutes(18), Guid.NewGuid(), orderId),
            TestContext.Current.CancellationToken);

        // Assert
        OrderFact fact = await WaitForOrderFactAsync(
            orderId,
            f => f.TimesUnassigned == 1,
            "an exhausted candidate list is a property of the order",
            TestContext.Current.CancellationToken);

        fact.Status.Should().Be(OrderFactStatus.Placed, "the order is still live and will be re-offered");
        fact.LastUnassignedOnUtc.Should().NotBeNull();
    }
}
