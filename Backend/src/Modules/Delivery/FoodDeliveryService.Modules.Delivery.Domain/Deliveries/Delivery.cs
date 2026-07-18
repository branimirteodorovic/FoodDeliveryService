using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Domain.Shared;

namespace FoodDeliveryService.Modules.Delivery.Domain.Deliveries;

/// <summary>
/// Aggregate root for the delivery leg of one order — the record of truth for assignment. The
/// offer → accept/reject → timeout → next-nearest loop is modeled here directly: which drivers
/// have already been tried and when the current offer lapses are columns on this aggregate, so
/// offer timeouts are inherently durable (ProcessExpiredOffersJob re-derives what's expired from
/// the database on each tick) and the whole record stays Dapper-queryable for the tracking screen
/// (Feature 2.2). The pickup location is snapshotted from OrderReadyForPickup so re-offers don't
/// depend on the event still being around.
/// </summary>
public sealed class Delivery : Entity
{
    // Drivers already offered this delivery. Rejection and expiry return the delivery to Pending,
    // but this list persists — the same driver is never offered the same delivery twice.
    private readonly List<Guid> _triedDriverIds = [];

    private Delivery()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid RestaurantId { get; private set; }

    public Guid CustomerId { get; private set; }

    public GeoCoordinate PickupLocation { get; private set; }

    public DeliveryAddress DropoffAddress { get; private set; }

    /// <summary>The driver who accepted — null until the delivery is Assigned.</summary>
    public Guid? DriverId { get; private set; }

    public DeliveryStatus Status { get; private set; }

    public Guid? OfferedDriverId { get; private set; }

    public DateTime? OfferExpiresOnUtc { get; private set; }

    public DateTime? AssignedOnUtc { get; private set; }

    public DateTime? PickedUpOnUtc { get; private set; }

    public DateTime? DeliveredOnUtc { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public IReadOnlyCollection<Guid> TriedDriverIds => _triedDriverIds.AsReadOnly();

    public static Delivery Create(
        Guid orderId,
        Guid restaurantId,
        Guid customerId,
        GeoCoordinate pickupLocation,
        DeliveryAddress dropoffAddress,
        DateTime utcNow)
    {
        var delivery = new Delivery
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            RestaurantId = restaurantId,
            CustomerId = customerId,
            PickupLocation = pickupLocation,
            DropoffAddress = dropoffAddress,
            Status = DeliveryStatus.Pending,
            CreatedOnUtc = utcNow
        };

        delivery.Raise(new DeliveryCreatedDomainEvent(delivery.Id, orderId));

        return delivery;
    }

    /// <summary>
    /// The candidate-selection step of the offer routine: given the store's distance-ordered
    /// candidates, the next driver to offer to is the nearest one this delivery has not already
    /// tried — or null when every candidate has been. Pure function over the aggregate's state.
    /// </summary>
    public Guid? SelectNextCandidate(IEnumerable<Guid> candidatesNearestFirst)
    {
        ArgumentNullException.ThrowIfNull(candidatesNearestFirst);

        foreach (Guid candidateId in candidatesNearestFirst)
        {
            if (!_triedDriverIds.Contains(candidateId))
            {
                return candidateId;
            }
        }

        return null;
    }

    public Result OfferTo(Guid driverId, DateTime expiresOnUtc)
    {
        if (Status is not (DeliveryStatus.Pending or DeliveryStatus.Offered))
        {
            return Result.Failure(DeliveryErrors.InvalidTransition(Status, DeliveryStatus.Offered));
        }

        if (_triedDriverIds.Contains(driverId))
        {
            return Result.Failure(DeliveryErrors.DriverAlreadyTried(driverId));
        }

        _triedDriverIds.Add(driverId);
        OfferedDriverId = driverId;
        OfferExpiresOnUtc = expiresOnUtc;
        Status = DeliveryStatus.Offered;

        Raise(new DeliveryOfferedDomainEvent(Id, OrderId, driverId, expiresOnUtc));

        return Result.Success();
    }

    public Result AcceptOffer(Guid driverId, DateTime utcNow)
    {
        if (Status != DeliveryStatus.Offered)
        {
            return Result.Failure(DeliveryErrors.InvalidTransition(Status, DeliveryStatus.Assigned));
        }

        if (OfferedDriverId != driverId)
        {
            return Result.Failure(DeliveryErrors.NotAssignedDriver);
        }

        if (utcNow > OfferExpiresOnUtc)
        {
            return Result.Failure(DeliveryErrors.OfferExpired);
        }

        DriverId = driverId;
        AssignedOnUtc = utcNow;
        OfferedDriverId = null;
        OfferExpiresOnUtc = null;
        Status = DeliveryStatus.Assigned;

        Raise(new DeliveryAssignedDomainEvent(Id, OrderId, driverId, utcNow));

        return Result.Success();
    }

    public Result RejectOffer(Guid driverId)
    {
        if (Status != DeliveryStatus.Offered)
        {
            return Result.Failure(DeliveryErrors.InvalidTransition(Status, DeliveryStatus.Pending));
        }

        if (OfferedDriverId != driverId)
        {
            return Result.Failure(DeliveryErrors.NotAssignedDriver);
        }

        OfferedDriverId = null;
        OfferExpiresOnUtc = null;
        Status = DeliveryStatus.Pending;

        Raise(new DeliveryOfferRejectedDomainEvent(Id, OrderId, driverId));

        return Result.Success();
    }

    public Result ExpireOffer(DateTime utcNow)
    {
        if (Status != DeliveryStatus.Offered)
        {
            return Result.Failure(DeliveryErrors.InvalidTransition(Status, DeliveryStatus.Pending));
        }

        if (utcNow < OfferExpiresOnUtc)
        {
            return Result.Failure(DeliveryErrors.OfferNotExpired);
        }

        Guid expiredDriverId = OfferedDriverId!.Value;

        OfferedDriverId = null;
        OfferExpiresOnUtc = null;
        Status = DeliveryStatus.Pending;

        Raise(new DeliveryOfferExpiredDomainEvent(Id, OrderId, expiredDriverId));

        return Result.Success();
    }

    /// <summary>Candidates exhausted — parked for a later manual admin/support re-offer.</summary>
    public Result MarkUnassigned()
    {
        if (Status is not (DeliveryStatus.Pending or DeliveryStatus.Offered))
        {
            return Result.Failure(DeliveryErrors.InvalidTransition(Status, DeliveryStatus.Unassigned));
        }

        OfferedDriverId = null;
        OfferExpiresOnUtc = null;
        Status = DeliveryStatus.Unassigned;

        Raise(new DeliveryUnassignedDomainEvent(Id, OrderId));

        return Result.Success();
    }

    public Result MarkPickedUp(Guid driverId, DateTime utcNow)
    {
        if (Status != DeliveryStatus.Assigned)
        {
            return Result.Failure(DeliveryErrors.InvalidTransition(Status, DeliveryStatus.PickedUp));
        }

        if (DriverId != driverId)
        {
            return Result.Failure(DeliveryErrors.NotAssignedDriver);
        }

        PickedUpOnUtc = utcNow;
        Status = DeliveryStatus.PickedUp;

        Raise(new DeliveryPickedUpDomainEvent(Id, OrderId, driverId));

        return Result.Success();
    }

    public Result MarkDelivered(Guid driverId, DateTime utcNow)
    {
        if (Status != DeliveryStatus.PickedUp)
        {
            return Result.Failure(DeliveryErrors.InvalidTransition(Status, DeliveryStatus.Delivered));
        }

        if (DriverId != driverId)
        {
            return Result.Failure(DeliveryErrors.NotAssignedDriver);
        }

        DeliveredOnUtc = utcNow;
        Status = DeliveryStatus.Delivered;

        Raise(new DeliveryDeliveredDomainEvent(Id, OrderId, driverId));

        return Result.Success();
    }

    /// <summary>
    /// Compensation for a cancelled order. A no-op — raising NO event — when the delivery is
    /// already terminal, so a replayed OrderCancelled event (or one racing the last mile) settles
    /// idempotently instead of failing the inbox. DriverId is retained so the caller can release
    /// a reserved driver back to the pool.
    /// </summary>
    public Result Cancel()
    {
        if (Status is DeliveryStatus.Delivered or DeliveryStatus.Cancelled)
        {
            return Result.Success();
        }

        OfferedDriverId = null;
        OfferExpiresOnUtc = null;
        Status = DeliveryStatus.Cancelled;

        Raise(new DeliveryCancelledDomainEvent(Id, OrderId));

        return Result.Success();
    }
}
