using AwesomeAssertions;
using FoodDeliveryService.Modules.FraudDetection.Domain.Customers;
using FoodDeliveryService.Modules.FraudDetection.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.FraudDetection.UnitTests.Customers;

public class CustomerBehaviourTests : BaseTest
{
    private static CustomerBehaviour Create(DateTime? firstSeenOnUtc = null) =>
        CustomerBehaviour.Create(Guid.NewGuid(), firstSeenOnUtc ?? Now);

    [Fact]
    public void Create_Should_StartEmptyWithUnknownRegistration()
    {
        CustomerBehaviour behaviour = Create();

        behaviour.RegisteredOnUtc.Should().BeNull(
            "the registration event may arrive after the first order, and an unknown account age " +
            "must never read as a new one");
        behaviour.FirstSeenOnUtc.Should().Be(Now);
        behaviour.WindowStartedOnUtc.Should().Be(Now);
        behaviour.OrdersPlaced.Should().Be(0);
        behaviour.TotalOrderValue.Should().Be(0m);
        behaviour.LastOrderOnUtc.Should().BeNull();
    }

    [Fact]
    public void Register_Should_RecordAccountAge_And_PullFirstSeenBack()
    {
        // The customer was seen ordering before FraudDetection consumed their registration.
        CustomerBehaviour behaviour = Create();

        DateTime registeredOnUtc = Now.AddDays(-30);

        behaviour.Register(registeredOnUtc);

        behaviour.RegisteredOnUtc.Should().Be(registeredOnUtc);
        behaviour.FirstSeenOnUtc.Should().Be(registeredOnUtc, "first-seen is a floor and only moves earlier");
    }

    [Fact]
    public void Register_Should_BeIdempotent_OnRedelivery()
    {
        CustomerBehaviour behaviour = Create();

        behaviour.Register(Now.AddDays(-30));
        behaviour.Register(Now.AddDays(-1));

        behaviour.RegisteredOnUtc.Should().Be(Now.AddDays(-30), "a known registration date cannot be moved");
    }

    [Fact]
    public void RecordOrderPlaced_Should_AdvanceLifetimeAndWindowCounters()
    {
        CustomerBehaviour behaviour = Create();

        behaviour.RecordOrderPlaced(24.50m, Now, Window);
        behaviour.RecordOrderPlaced(10.00m, Now.AddHours(1), Window);

        behaviour.OrdersPlaced.Should().Be(2);
        behaviour.OrdersPlacedInWindow.Should().Be(2);
        behaviour.TotalOrderValue.Should().Be(34.50m);
        behaviour.LastOrderOnUtc.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void RecordOrderPlaced_Should_NotMoveLastOrder_Backwards()
    {
        CustomerBehaviour behaviour = Create();

        behaviour.RecordOrderPlaced(10m, Now, Window);
        behaviour.RecordOrderPlaced(10m, Now.AddHours(-3), Window);

        behaviour.LastOrderOnUtc.Should().Be(Now);
    }

    [Fact]
    public void RecordOrderCancelled_Should_CountBeforePickup_Separately()
    {
        CustomerBehaviour behaviour = Create();

        behaviour.RecordOrderCancelled(Now, beforePickup: true, Window);
        behaviour.RecordOrderCancelled(Now.AddMinutes(5), beforePickup: false, Window);

        behaviour.OrdersCancelled.Should().Be(2);
        behaviour.OrdersCancelledInWindow.Should().Be(2);
        behaviour.CancelledBeforePickup.Should().Be(1);
    }

    [Fact]
    public void Counters_Should_Reset_WhenTheWindowLapses()
    {
        CustomerBehaviour behaviour = Create();

        behaviour.RecordOrderPlaced(10m, Now, Window);
        behaviour.RecordOrderCancelled(Now.AddHours(1), beforePickup: true, Window);

        // One second past the window.
        behaviour.RecordOrderPlaced(10m, Now.Add(Window).AddSeconds(1), Window);

        behaviour.OrdersPlacedInWindow.Should().Be(1, "the lapsed window's counters were reset");
        behaviour.OrdersCancelledInWindow.Should().Be(0);
        behaviour.WindowStartedOnUtc.Should().Be(Now.Add(Window).AddSeconds(1));

        behaviour.OrdersPlaced.Should().Be(2, "lifetime counters never reset");
        behaviour.OrdersCancelled.Should().Be(1);
        behaviour.CancelledBeforePickup.Should().Be(1);
    }

    [Fact]
    public void Counters_Should_NotReset_ExactlyOnTheWindowBoundary()
    {
        CustomerBehaviour behaviour = Create();

        behaviour.RecordOrderPlaced(10m, Now, Window);
        behaviour.RecordOrderPlaced(10m, Now.Add(Window), Window);

        behaviour.OrdersPlacedInWindow.Should().Be(2, "the window is inclusive of its own length");
        behaviour.WindowStartedOnUtc.Should().Be(Now);
    }

    [Fact]
    public void Counters_Should_NotReset_ForALateEventInsideTheCurrentWindow()
    {
        CustomerBehaviour behaviour = Create(Now);

        behaviour.RecordOrderPlaced(10m, Now.AddHours(2), Window);

        // A redelivery of something older than the window start, but not newer than it — counting it
        // into the live window is cheaper than resetting an active customer's rate to zero.
        behaviour.RecordOrderPlaced(10m, Now.AddHours(-48), Window);

        behaviour.OrdersPlacedInWindow.Should().Be(2);
        behaviour.WindowStartedOnUtc.Should().Be(Now);
    }
}
