using AwesomeAssertions;
using FoodDeliveryService.Modules.Orders.Domain.Customers;
using FoodDeliveryService.Modules.Orders.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.Orders.UnitTests.Customers;

/// <summary>
/// The one-flag replica of "this customer can pay by card" — Feature 3.8 Milestone D, §6.3.
/// <para>
/// There is almost nothing here to test except the one rule that is easy to leave out and expensive
/// to leave out: last-write-wins by the <em>source</em> timestamp rather than by arrival. MassTransit
/// guarantees no ordering between two messages, and an attach delivered after the detach that
/// superseded it would leave a customer being offered a card they removed — a defect that reproduces
/// only under load and looks like a phantom card.
/// </para>
/// </summary>
public class CustomerPaymentProfileTests : BaseTest
{
    private static readonly DateTime Attached = new(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_Should_RecordTheFlagAndItsTimestamp()
    {
        // Act
        var profile = CustomerPaymentProfile.Create(Guid.NewGuid(), true, Attached);

        // Assert — a projection of another service's state raises nothing: Payments already
        // published the originating event.
        profile.CanPayByCard.Should().BeTrue();
        profile.ChangedOnUtc.Should().Be(Attached);
        profile.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Apply_Should_TakeALaterChange()
    {
        // Arrange
        var profile = CustomerPaymentProfile.Create(Guid.NewGuid(), true, Attached);

        // Act
        profile.Apply(canPayByCard: false, Attached.AddMinutes(1));

        // Assert
        profile.CanPayByCard.Should().BeFalse();
        profile.ChangedOnUtc.Should().Be(Attached.AddMinutes(1));
    }

    [Fact]
    public void Apply_Should_IgnoreAnEarlierChange()
    {
        // Arrange — the detach has already been projected; the attach it superseded arrives late.
        var profile = CustomerPaymentProfile.Create(Guid.NewGuid(), false, Attached);

        // Act
        profile.Apply(canPayByCard: true, Attached.AddMinutes(-1));

        // Assert
        profile.CanPayByCard.Should().BeFalse("a late attach must not resurrect a card that was removed");
        profile.ChangedOnUtc.Should().Be(Attached);
    }

    [Fact]
    public void Apply_Should_TakeAChangeWithTheSameTimestamp()
    {
        // Arrange — a redelivery of the event already applied. Idempotent, so it is accepted rather
        // than refused: the alternative is an inequality that also rejects a legitimate second event
        // in the same clock tick.
        var profile = CustomerPaymentProfile.Create(Guid.NewGuid(), true, Attached);

        // Act
        profile.Apply(canPayByCard: true, Attached);

        // Assert
        profile.CanPayByCard.Should().BeTrue();
        profile.ChangedOnUtc.Should().Be(Attached);
    }
}
