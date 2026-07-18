using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;
using FoodDeliveryService.Modules.Delivery.Domain.Shared;
using FoodDeliveryService.Modules.Delivery.UnitTests.Abstractions;
using DeliveryAggregate = FoodDeliveryService.Modules.Delivery.Domain.Deliveries.Delivery;

namespace FoodDeliveryService.Modules.Delivery.UnitTests.Deliveries;

public class DeliveryTests : BaseTest
{
    private static readonly DateTime UtcNow = DateTime.UtcNow;
    private static readonly DateTime OfferDeadline = UtcNow.AddSeconds(30);

    private static DeliveryAggregate CreateDelivery(Guid? orderId = null)
    {
        return DeliveryAggregate.Create(
            orderId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            GeoCoordinate.Create(Faker.Address.Latitude(), Faker.Address.Longitude()).Value,
            new DeliveryAddress(
                Faker.Address.StreetAddress(),
                Faker.Address.City(),
                Faker.Address.ZipCode(),
                Faker.Address.Country(),
                null,
                Faker.Address.Latitude(),
                Faker.Address.Longitude()),
            UtcNow);
    }

    private static DeliveryAggregate CreateOfferedDelivery(Guid driverId)
    {
        DeliveryAggregate delivery = CreateDelivery();
        delivery.OfferTo(driverId, OfferDeadline);
        return delivery;
    }

    private static DeliveryAggregate CreateAssignedDelivery(Guid driverId)
    {
        DeliveryAggregate delivery = CreateOfferedDelivery(driverId);
        delivery.AcceptOffer(driverId, UtcNow);
        return delivery;
    }

    [Fact]
    public void Create_ShouldStartPending_AndRaiseDeliveryCreatedDomainEvent()
    {
        // Arrange
        var orderId = Guid.NewGuid();

        // Act
        DeliveryAggregate delivery = CreateDelivery(orderId);

        // Assert
        delivery.Status.Should().Be(DeliveryStatus.Pending);
        delivery.OrderId.Should().Be(orderId);
        delivery.DriverId.Should().BeNull();
        delivery.TriedDriverIds.Should().BeEmpty();

        DeliveryCreatedDomainEvent domainEvent =
            AssertDomainEventWasPublished<DeliveryCreatedDomainEvent>(delivery);

        domainEvent.DeliveryId.Should().Be(delivery.Id);
        domainEvent.OrderId.Should().Be(orderId);
    }

    [Fact]
    public void OfferTo_ShouldRecordTheOffer_AndRaiseDeliveryOfferedDomainEvent()
    {
        // Arrange
        DeliveryAggregate delivery = CreateDelivery();
        var driverId = Guid.NewGuid();

        // Act
        Result result = delivery.OfferTo(driverId, OfferDeadline);

        // Assert
        result.IsSuccess.Should().BeTrue();
        delivery.Status.Should().Be(DeliveryStatus.Offered);
        delivery.OfferedDriverId.Should().Be(driverId);
        delivery.OfferExpiresOnUtc.Should().Be(OfferDeadline);
        delivery.TriedDriverIds.Should().ContainSingle().Which.Should().Be(driverId);

        DeliveryOfferedDomainEvent domainEvent =
            AssertDomainEventWasPublished<DeliveryOfferedDomainEvent>(delivery);

        domainEvent.DriverId.Should().Be(driverId);
        domainEvent.OfferExpiresOnUtc.Should().Be(OfferDeadline);
    }

    [Fact]
    public void OfferTo_ShouldFail_WhenDriverWasAlreadyTried()
    {
        // Arrange — reject returns the delivery to Pending, but the tried list persists.
        var driverId = Guid.NewGuid();
        DeliveryAggregate delivery = CreateOfferedDelivery(driverId);
        delivery.RejectOffer(driverId);

        // Act
        Result result = delivery.OfferTo(driverId, OfferDeadline);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Deliveries.DriverAlreadyTried");
        delivery.Status.Should().Be(DeliveryStatus.Pending);
    }

    [Fact]
    public void OfferTo_ShouldFail_WhenDeliveryIsAssigned()
    {
        // Arrange
        DeliveryAggregate delivery = CreateAssignedDelivery(Guid.NewGuid());

        // Act
        Result result = delivery.OfferTo(Guid.NewGuid(), OfferDeadline);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Deliveries.InvalidTransition");
    }

    [Fact]
    public void AcceptOffer_ShouldAssignTheDriver_AndRaiseDeliveryAssignedDomainEvent()
    {
        // Arrange
        var driverId = Guid.NewGuid();
        DeliveryAggregate delivery = CreateOfferedDelivery(driverId);

        // Act
        Result result = delivery.AcceptOffer(driverId, UtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        delivery.Status.Should().Be(DeliveryStatus.Assigned);
        delivery.DriverId.Should().Be(driverId);
        delivery.AssignedOnUtc.Should().Be(UtcNow);
        delivery.OfferedDriverId.Should().BeNull();
        delivery.OfferExpiresOnUtc.Should().BeNull();

        DeliveryAssignedDomainEvent domainEvent =
            AssertDomainEventWasPublished<DeliveryAssignedDomainEvent>(delivery);

        domainEvent.DriverId.Should().Be(driverId);
        domainEvent.AssignedOnUtc.Should().Be(UtcNow);
    }

    [Fact]
    public void AcceptOffer_ShouldFail_WhenCallerIsNotTheOfferedDriver()
    {
        // Arrange
        DeliveryAggregate delivery = CreateOfferedDelivery(Guid.NewGuid());

        // Act
        Result result = delivery.AcceptOffer(Guid.NewGuid(), UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DeliveryErrors.NotAssignedDriver);
        delivery.Status.Should().Be(DeliveryStatus.Offered);
    }

    [Fact]
    public void AcceptOffer_ShouldFail_WhenOfferHasExpired()
    {
        // Arrange
        var driverId = Guid.NewGuid();
        DeliveryAggregate delivery = CreateOfferedDelivery(driverId);

        // Act
        Result result = delivery.AcceptOffer(driverId, OfferDeadline.AddSeconds(1));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DeliveryErrors.OfferExpired);
        delivery.Status.Should().Be(DeliveryStatus.Offered);
    }

    [Fact]
    public void AcceptOffer_ShouldFail_WhenDeliveryIsNotOffered()
    {
        // Arrange
        DeliveryAggregate delivery = CreateDelivery();

        // Act
        Result result = delivery.AcceptOffer(Guid.NewGuid(), UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Deliveries.InvalidTransition");
    }

    [Fact]
    public void RejectOffer_ShouldReturnToPending_AndRaiseDeliveryOfferRejectedDomainEvent()
    {
        // Arrange
        var driverId = Guid.NewGuid();
        DeliveryAggregate delivery = CreateOfferedDelivery(driverId);

        // Act
        Result result = delivery.RejectOffer(driverId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        delivery.Status.Should().Be(DeliveryStatus.Pending);
        delivery.OfferedDriverId.Should().BeNull();
        delivery.OfferExpiresOnUtc.Should().BeNull();
        delivery.TriedDriverIds.Should().ContainSingle().Which.Should().Be(driverId);

        DeliveryOfferRejectedDomainEvent domainEvent =
            AssertDomainEventWasPublished<DeliveryOfferRejectedDomainEvent>(delivery);

        domainEvent.DriverId.Should().Be(driverId);
    }

    [Fact]
    public void RejectOffer_ShouldFail_WhenCallerIsNotTheOfferedDriver()
    {
        // Arrange
        DeliveryAggregate delivery = CreateOfferedDelivery(Guid.NewGuid());

        // Act
        Result result = delivery.RejectOffer(Guid.NewGuid());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DeliveryErrors.NotAssignedDriver);
    }

    [Fact]
    public void RejectOffer_ShouldAllowOfferingToADifferentDriver()
    {
        // Arrange
        var firstDriverId = Guid.NewGuid();
        var secondDriverId = Guid.NewGuid();
        DeliveryAggregate delivery = CreateOfferedDelivery(firstDriverId);
        delivery.RejectOffer(firstDriverId);

        // Act
        Result result = delivery.OfferTo(secondDriverId, OfferDeadline);

        // Assert
        result.IsSuccess.Should().BeTrue();
        delivery.Status.Should().Be(DeliveryStatus.Offered);
        delivery.OfferedDriverId.Should().Be(secondDriverId);
        delivery.TriedDriverIds.Should().BeEquivalentTo([firstDriverId, secondDriverId]);
    }

    [Fact]
    public void ExpireOffer_ShouldReturnToPending_AndRaiseDeliveryOfferExpiredDomainEvent()
    {
        // Arrange
        var driverId = Guid.NewGuid();
        DeliveryAggregate delivery = CreateOfferedDelivery(driverId);

        // Act
        Result result = delivery.ExpireOffer(OfferDeadline.AddSeconds(1));

        // Assert
        result.IsSuccess.Should().BeTrue();
        delivery.Status.Should().Be(DeliveryStatus.Pending);
        delivery.OfferedDriverId.Should().BeNull();
        delivery.OfferExpiresOnUtc.Should().BeNull();
        delivery.TriedDriverIds.Should().ContainSingle().Which.Should().Be(driverId);

        DeliveryOfferExpiredDomainEvent domainEvent =
            AssertDomainEventWasPublished<DeliveryOfferExpiredDomainEvent>(delivery);

        domainEvent.DriverId.Should().Be(driverId);
    }

    [Fact]
    public void ExpireOffer_ShouldFail_WhenOfferHasNotLapsedYet()
    {
        // Arrange
        DeliveryAggregate delivery = CreateOfferedDelivery(Guid.NewGuid());

        // Act
        Result result = delivery.ExpireOffer(OfferDeadline.AddSeconds(-1));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DeliveryErrors.OfferNotExpired);
        delivery.Status.Should().Be(DeliveryStatus.Offered);
    }

    [Fact]
    public void ExpireOffer_ShouldFail_WhenDeliveryIsNotOffered()
    {
        // Arrange
        DeliveryAggregate delivery = CreateDelivery();

        // Act
        Result result = delivery.ExpireOffer(UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Deliveries.InvalidTransition");
    }

    [Theory]
    [InlineData(DeliveryStatus.Pending)]
    [InlineData(DeliveryStatus.Offered)]
    public void MarkUnassigned_ShouldPark_FromPendingOrOffered(DeliveryStatus from)
    {
        // Arrange
        DeliveryAggregate delivery = from == DeliveryStatus.Offered
            ? CreateOfferedDelivery(Guid.NewGuid())
            : CreateDelivery();

        // Act
        Result result = delivery.MarkUnassigned();

        // Assert
        result.IsSuccess.Should().BeTrue();
        delivery.Status.Should().Be(DeliveryStatus.Unassigned);
        delivery.OfferedDriverId.Should().BeNull();

        AssertDomainEventWasPublished<DeliveryUnassignedDomainEvent>(delivery);
    }

    [Fact]
    public void MarkUnassigned_ShouldFail_WhenDeliveryIsAssigned()
    {
        // Arrange
        DeliveryAggregate delivery = CreateAssignedDelivery(Guid.NewGuid());

        // Act
        Result result = delivery.MarkUnassigned();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Deliveries.InvalidTransition");
    }

    [Fact]
    public void MarkPickedUp_ShouldAdvance_AndRaiseDeliveryPickedUpDomainEvent()
    {
        // Arrange
        var driverId = Guid.NewGuid();
        DeliveryAggregate delivery = CreateAssignedDelivery(driverId);

        // Act
        Result result = delivery.MarkPickedUp(driverId, UtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        delivery.Status.Should().Be(DeliveryStatus.PickedUp);
        delivery.PickedUpOnUtc.Should().Be(UtcNow);

        AssertDomainEventWasPublished<DeliveryPickedUpDomainEvent>(delivery);
    }

    [Fact]
    public void MarkPickedUp_ShouldFail_WhenCallerIsNotTheAssignedDriver()
    {
        // Arrange
        DeliveryAggregate delivery = CreateAssignedDelivery(Guid.NewGuid());

        // Act
        Result result = delivery.MarkPickedUp(Guid.NewGuid(), UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DeliveryErrors.NotAssignedDriver);
    }

    [Fact]
    public void MarkDelivered_ShouldComplete_AndRaiseDeliveryDeliveredDomainEvent()
    {
        // Arrange
        var driverId = Guid.NewGuid();
        DeliveryAggregate delivery = CreateAssignedDelivery(driverId);
        delivery.MarkPickedUp(driverId, UtcNow);

        // Act
        Result result = delivery.MarkDelivered(driverId, UtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        delivery.Status.Should().Be(DeliveryStatus.Delivered);
        delivery.DeliveredOnUtc.Should().Be(UtcNow);

        AssertDomainEventWasPublished<DeliveryDeliveredDomainEvent>(delivery);
    }

    [Fact]
    public void MarkDelivered_ShouldFail_WhenPickupWasSkipped()
    {
        // Arrange
        var driverId = Guid.NewGuid();
        DeliveryAggregate delivery = CreateAssignedDelivery(driverId);

        // Act
        Result result = delivery.MarkDelivered(driverId, UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Deliveries.InvalidTransition");
    }

    [Fact]
    public void MarkDelivered_ShouldFail_WhenCallerIsNotTheAssignedDriver()
    {
        // Arrange
        var driverId = Guid.NewGuid();
        DeliveryAggregate delivery = CreateAssignedDelivery(driverId);
        delivery.MarkPickedUp(driverId, UtcNow);

        // Act
        Result result = delivery.MarkDelivered(Guid.NewGuid(), UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DeliveryErrors.NotAssignedDriver);
    }

    [Theory]
    [InlineData(DeliveryStatus.Pending)]
    [InlineData(DeliveryStatus.Offered)]
    [InlineData(DeliveryStatus.Assigned)]
    [InlineData(DeliveryStatus.PickedUp)]
    [InlineData(DeliveryStatus.Unassigned)]
    public void Cancel_ShouldCancel_FromAnyNonTerminalStatus(DeliveryStatus from)
    {
        // Arrange
        var driverId = Guid.NewGuid();
        DeliveryAggregate delivery = CreateDelivery();

        switch (from)
        {
            case DeliveryStatus.Offered:
                delivery.OfferTo(driverId, OfferDeadline);
                break;
            case DeliveryStatus.Assigned:
                delivery.OfferTo(driverId, OfferDeadline);
                delivery.AcceptOffer(driverId, UtcNow);
                break;
            case DeliveryStatus.PickedUp:
                delivery.OfferTo(driverId, OfferDeadline);
                delivery.AcceptOffer(driverId, UtcNow);
                delivery.MarkPickedUp(driverId, UtcNow);
                break;
            case DeliveryStatus.Unassigned:
                delivery.MarkUnassigned();
                break;
        }

        // Act
        Result result = delivery.Cancel();

        // Assert
        result.IsSuccess.Should().BeTrue();
        delivery.Status.Should().Be(DeliveryStatus.Cancelled);

        AssertDomainEventWasPublished<DeliveryCancelledDomainEvent>(delivery);
    }

    [Fact]
    public void Cancel_ShouldBeANoOp_WhenDeliveryIsAlreadyTerminal()
    {
        // Arrange
        var driverId = Guid.NewGuid();
        DeliveryAggregate delivery = CreateAssignedDelivery(driverId);
        delivery.MarkPickedUp(driverId, UtcNow);
        delivery.MarkDelivered(driverId, UtcNow);

        // Act
        Result result = delivery.Cancel();

        // Assert — success, but no state change and NO event: a replayed OrderCancelled must not
        // un-deliver a delivery or spam the outbox.
        result.IsSuccess.Should().BeTrue();
        delivery.Status.Should().Be(DeliveryStatus.Delivered);
        delivery.DomainEvents.OfType<DeliveryCancelledDomainEvent>().Should().BeEmpty();
    }

    [Fact]
    public void SelectNextCandidate_ShouldPickTheNearestUntriedDriver()
    {
        // Arrange — candidates arrive distance-ordered from the location store.
        var nearest = Guid.NewGuid();
        var farther = Guid.NewGuid();
        DeliveryAggregate delivery = CreateDelivery();

        // Act
        Guid? candidate = delivery.SelectNextCandidate([nearest, farther]);

        // Assert
        candidate.Should().Be(nearest);
    }

    [Fact]
    public void SelectNextCandidate_ShouldSkipDriversAlreadyTried()
    {
        // Arrange
        var nearest = Guid.NewGuid();
        var farther = Guid.NewGuid();
        DeliveryAggregate delivery = CreateOfferedDelivery(nearest);
        delivery.RejectOffer(nearest);

        // Act
        Guid? candidate = delivery.SelectNextCandidate([nearest, farther]);

        // Assert
        candidate.Should().Be(farther);
    }

    [Fact]
    public void SelectNextCandidate_ShouldReturnNull_WhenAllCandidatesWereTried()
    {
        // Arrange
        var onlyDriver = Guid.NewGuid();
        DeliveryAggregate delivery = CreateOfferedDelivery(onlyDriver);
        delivery.RejectOffer(onlyDriver);

        // Act
        Guid? candidate = delivery.SelectNextCandidate([onlyDriver]);

        // Assert
        candidate.Should().BeNull();
    }

    [Fact]
    public void SelectNextCandidate_ShouldReturnNull_WhenThereAreNoCandidates()
    {
        // Arrange
        DeliveryAggregate delivery = CreateDelivery();

        // Act
        Guid? candidate = delivery.SelectNextCandidate([]);

        // Assert
        candidate.Should().BeNull();
    }
}
