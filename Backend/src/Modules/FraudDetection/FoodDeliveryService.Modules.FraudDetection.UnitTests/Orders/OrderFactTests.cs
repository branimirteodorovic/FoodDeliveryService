using AwesomeAssertions;
using FoodDeliveryService.Modules.FraudDetection.Domain.Orders;
using FoodDeliveryService.Modules.FraudDetection.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.FraudDetection.UnitTests.Orders;

public class OrderFactTests : BaseTest
{
    private static OrderFact Create() =>
        OrderFact.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 42.00m, Now);

    [Fact]
    public void Create_Should_StartPlacedWithNoDropoffCoordinates()
    {
        OrderFact fact = Create();

        fact.Status.Should().Be(OrderFactStatus.Placed);
        fact.PlacedOnUtc.Should().Be(Now);
        fact.IsOpen.Should().BeTrue();
        fact.DropoffLatitude.Should().BeNull("OrderPlaced does not carry them — only OrderReadyForPickup does");
        fact.DropoffLongitude.Should().BeNull();
        fact.CancelledBeforePickup.Should().BeFalse();
    }

    [Fact]
    public void MarkReadyForPickup_Should_CaptureTheDropoffCoordinates()
    {
        OrderFact fact = Create();

        fact.MarkAccepted(Now.AddMinutes(2));
        fact.MarkReadyForPickup(Now.AddMinutes(15), 44.7866, 20.4489);

        fact.Status.Should().Be(OrderFactStatus.ReadyForPickup);
        fact.DropoffLatitude.Should().Be(44.7866);
        fact.DropoffLongitude.Should().Be(20.4489);
    }

    [Fact]
    public void FullHappyPath_Should_EndDeliveredWithTheDriverBound()
    {
        OrderFact fact = Create();
        var deliveryId = Guid.NewGuid();
        var driverId = Guid.NewGuid();

        fact.MarkAccepted(Now.AddMinutes(2));
        fact.MarkReadyForPickup(Now.AddMinutes(15), 44.78, 20.44);
        fact.MarkPickedUp(deliveryId, driverId, Now.AddMinutes(20));
        fact.MarkDelivered(deliveryId, driverId, Now.AddMinutes(45));

        fact.Status.Should().Be(OrderFactStatus.Delivered);
        fact.IsOpen.Should().BeFalse();
        fact.DeliveryId.Should().Be(deliveryId);
        fact.DriverId.Should().Be(driverId);
        fact.PickedUpOnUtc.Should().Be(Now.AddMinutes(20));
        fact.DeliveredOnUtc.Should().Be(Now.AddMinutes(45));
    }

    [Fact]
    public void MarkCancelled_AfterAcceptance_Should_FlagCancelledBeforePickup()
    {
        OrderFact fact = Create();

        fact.MarkAccepted(Now.AddMinutes(2));
        fact.MarkCancelled(Now.AddMinutes(10));

        fact.Status.Should().Be(OrderFactStatus.Cancelled);
        fact.CancelledBeforePickup.Should().BeTrue("the restaurant had already committed to the order");
    }

    [Fact]
    public void MarkCancelled_BeforeAcceptance_Should_NotFlagCancelledBeforePickup()
    {
        OrderFact fact = Create();

        fact.MarkCancelled(Now.AddMinutes(1));

        fact.CancelledBeforePickup.Should().BeFalse("nobody had committed anything yet — this is not the abuse shape");
    }

    [Fact]
    public void MarkCancelled_AfterPickup_Should_NotFlagCancelledBeforePickup()
    {
        OrderFact fact = Create();

        fact.MarkAccepted(Now.AddMinutes(2));
        fact.MarkReadyForPickup(Now.AddMinutes(15), 44.78, 20.44);
        fact.MarkPickedUp(Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(20));
        fact.MarkCancelled(Now.AddMinutes(25));

        fact.Status.Should().Be(OrderFactStatus.Cancelled);
        fact.CancelledBeforePickup.Should().BeFalse();
    }

    [Fact]
    public void Transitions_Should_BeIgnored_AfterATerminalState()
    {
        OrderFact fact = Create();

        fact.MarkCancelled(Now.AddMinutes(5));
        fact.MarkAccepted(Now.AddMinutes(6));
        fact.MarkDelivered(Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(7));

        fact.Status.Should().Be(OrderFactStatus.Cancelled, "a projection ignores late events, it does not throw");
        fact.AcceptedOnUtc.Should().BeNull();
        fact.DeliveredOnUtc.Should().BeNull();
    }

    [Fact]
    public void Status_Should_NotMoveBackwards_OnALateEvent()
    {
        OrderFact fact = Create();

        fact.MarkAccepted(Now.AddMinutes(2));
        fact.MarkReadyForPickup(Now.AddMinutes(15), 44.78, 20.44);

        // A redelivered acceptance arriving after the order is already ready.
        fact.MarkAccepted(Now.AddMinutes(2));

        fact.Status.Should().Be(OrderFactStatus.ReadyForPickup);
    }

    [Fact]
    public void MarkReadyForPickup_Should_StillCaptureCoordinates_OnAClosedOrder()
    {
        OrderFact fact = Create();

        fact.MarkCancelled(Now.AddMinutes(5));
        fact.MarkReadyForPickup(Now.AddMinutes(6), 44.78, 20.44);

        fact.Status.Should().Be(OrderFactStatus.Cancelled, "the status is not advanced");
        fact.DropoffLatitude.Should().Be(44.78, "coordinates are reference data, not a transition");
    }

    [Fact]
    public void RecordUnassigned_Should_CountWithoutChangingStatus()
    {
        OrderFact fact = Create();

        fact.MarkAccepted(Now.AddMinutes(2));
        fact.MarkReadyForPickup(Now.AddMinutes(15), 44.78, 20.44);
        fact.RecordUnassigned(Now.AddMinutes(18));
        fact.RecordUnassigned(Now.AddMinutes(21));

        fact.TimesUnassigned.Should().Be(2);
        fact.LastUnassignedOnUtc.Should().Be(Now.AddMinutes(21));
        fact.Status.Should().Be(OrderFactStatus.ReadyForPickup, "the order is still live and will be re-offered");
    }
}
