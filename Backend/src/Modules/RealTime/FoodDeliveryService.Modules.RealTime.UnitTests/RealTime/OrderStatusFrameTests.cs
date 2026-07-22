using AwesomeAssertions;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using FoodDeliveryService.Modules.RealTime.Application.RealTime;

namespace FoodDeliveryService.Modules.RealTime.UnitTests.RealTime;

public class OrderStatusFrameTests
{
    private static readonly Guid OrderId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid RestaurantId = Guid.NewGuid();
    private static readonly DateTime OccurredOnUtc = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void From_OrderPlaced_MapsToPlacedFrame()
    {
        var integrationEvent = new OrderPlacedIntegrationEvent(
            Guid.NewGuid(), OccurredOnUtc, OrderId, CustomerId, RestaurantId, subtotal: 42m, placedOnUtc: OccurredOnUtc);

        var frame = OrderStatusFrame.From(integrationEvent);

        frame.Should().Be(new OrderStatusFrame(OrderId, OrderStatuses.Placed, OccurredOnUtc));
    }

    [Fact]
    public void From_OrderAccepted_MapsToAcceptedFrame()
    {
        var integrationEvent = new OrderAcceptedIntegrationEvent(
            Guid.NewGuid(), OccurredOnUtc, OrderId, CustomerId, RestaurantId, acceptedOnUtc: OccurredOnUtc);

        var frame = OrderStatusFrame.From(integrationEvent);

        frame.Should().Be(new OrderStatusFrame(OrderId, OrderStatuses.Accepted, OccurredOnUtc));
    }

    [Fact]
    public void From_OrderRejected_MapsToRejectedFrame()
    {
        var integrationEvent = new OrderRejectedIntegrationEvent(
            Guid.NewGuid(), OccurredOnUtc, OrderId, CustomerId, RestaurantId, reason: "Kitchen closed", rejectedOnUtc: OccurredOnUtc);

        var frame = OrderStatusFrame.From(integrationEvent);

        frame.Should().Be(new OrderStatusFrame(OrderId, OrderStatuses.Rejected, OccurredOnUtc));
    }

    [Fact]
    public void From_OrderReadyForPickup_MapsToReadyForPickupFrame()
    {
        var integrationEvent = new OrderReadyForPickupIntegrationEvent(
            Guid.NewGuid(), OccurredOnUtc, OrderId, CustomerId, RestaurantId,
            restaurantLatitude: 1, restaurantLongitude: 2,
            deliveryStreet: "1 Main St", deliveryCity: "Town", deliveryPostalCode: "0000", deliveryCountry: "Country",
            deliveryNotes: null, deliveryLatitude: 3, deliveryLongitude: 4, subtotal: 42m, placedOnUtc: OccurredOnUtc);

        var frame = OrderStatusFrame.From(integrationEvent);

        frame.Should().Be(new OrderStatusFrame(OrderId, OrderStatuses.ReadyForPickup, OccurredOnUtc));
    }

    [Fact]
    public void From_OrderCancelled_MapsToCancelledFrame()
    {
        var integrationEvent = new OrderCancelledIntegrationEvent(
            Guid.NewGuid(), OccurredOnUtc, OrderId, CustomerId, RestaurantId, cancelledOnUtc: OccurredOnUtc);

        var frame = OrderStatusFrame.From(integrationEvent);

        frame.Should().Be(new OrderStatusFrame(OrderId, OrderStatuses.Cancelled, OccurredOnUtc));
    }

    [Fact]
    public void From_AnOrdersEvent_LeavesTheDriverFieldsUnset()
    {
        // Driver name/vehicle arrive only from Milestone C (driver assignment); the Orders-owned
        // transitions never populate them.
        var integrationEvent = new OrderAcceptedIntegrationEvent(
            Guid.NewGuid(), OccurredOnUtc, OrderId, CustomerId, RestaurantId, acceptedOnUtc: OccurredOnUtc);

        var frame = OrderStatusFrame.From(integrationEvent);

        frame.DriverName.Should().BeNull();
        frame.DriverVehicle.Should().BeNull();
    }
}
