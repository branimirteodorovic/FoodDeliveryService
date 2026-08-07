using AwesomeAssertions;
using FoodDeliveryService.Modules.FraudDetection.Domain.Drivers;
using FoodDeliveryService.Modules.FraudDetection.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.FraudDetection.UnitTests.Drivers;

public class DriverBehaviourTests : BaseTest
{
    private static DriverBehaviour Create() => DriverBehaviour.Create(Guid.NewGuid(), Now);

    [Fact]
    public void Create_Should_StartEmpty()
    {
        DriverBehaviour behaviour = Create();

        behaviour.FirstSeenOnUtc.Should().Be(Now);
        behaviour.PickupsCompleted.Should().Be(0);
        behaviour.DeliveriesCompleted.Should().Be(0);
        behaviour.OffersRejected.Should().Be(0);
        behaviour.LocationMismatches.Should().Be(0);
        behaviour.LastDeliveryOnUtc.Should().BeNull();
    }

    [Fact]
    public void RecordDeliveryCompleted_Should_CountAndTrackTheLatestDelivery()
    {
        DriverBehaviour behaviour = Create();

        behaviour.RecordDeliveryCompleted(Now);
        behaviour.RecordDeliveryCompleted(Now.AddHours(2));

        behaviour.DeliveriesCompleted.Should().Be(2);
        behaviour.LastDeliveryOnUtc.Should().Be(Now.AddHours(2));
    }

    [Fact]
    public void RecordDeliveryCompleted_Should_NotMoveLastDelivery_Backwards()
    {
        DriverBehaviour behaviour = Create();

        behaviour.RecordDeliveryCompleted(Now);
        behaviour.RecordDeliveryCompleted(Now.AddHours(-5));

        behaviour.DeliveriesCompleted.Should().Be(2);
        behaviour.LastDeliveryOnUtc.Should().Be(Now);
    }

    [Fact]
    public void PickupAndOfferCounters_Should_BeIndependent()
    {
        DriverBehaviour behaviour = Create();

        behaviour.RecordPickup();
        behaviour.RecordOfferRejected();
        behaviour.RecordOfferRejected();
        behaviour.RecordLocationMismatch();

        behaviour.PickupsCompleted.Should().Be(1);
        behaviour.OffersRejected.Should().Be(2);
        behaviour.LocationMismatches.Should().Be(1);
        behaviour.DeliveriesCompleted.Should().Be(0, "a pickup is not a completed delivery");
    }
}
