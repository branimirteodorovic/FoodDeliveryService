using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.FraudDetection.Domain.Orders;

/// <summary>
/// The minimal per-order row the fraud signals need, reconstructed from the Orders and Delivery
/// lifecycle events. It is what makes an exact sliding window possible in Milestone B (every
/// timestamp is retained here) and what tells the cancellation handler whether a cancellation
/// landed <i>before pickup</i> — the order's status at cancellation is knowable only from FraudDetection's
/// own history of it, because the cancellation event does not carry it.
/// <para>
/// Transitions are guarded rather than validated: this is a projection of someone else's state
/// machine, so an event that arrives after a terminal one — a redelivery, or two queues racing —
/// is <b>ignored</b>, not rejected. There is no caller to return a <c>Result</c> to; the inbox does
/// not retry, and a projection that threw on a late duplicate would poison itself.
/// </para>
/// </summary>
public sealed class OrderFact : Entity
{
    private OrderFact()
    {
    }

    /// <summary>The Orders service's OrderId — never generated locally.</summary>
    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid RestaurantId { get; private set; }

    public decimal Subtotal { get; private set; }

    public DateTime PlacedOnUtc { get; private set; }

    public OrderFactStatus Status { get; private set; }

    public DateTime? AcceptedOnUtc { get; private set; }

    public DateTime? ReadyForPickupOnUtc { get; private set; }

    public DateTime? PickedUpOnUtc { get; private set; }

    public DateTime? DeliveredOnUtc { get; private set; }

    public DateTime? CancelledOnUtc { get; private set; }

    public DateTime? RejectedOnUtc { get; private set; }

    /// <summary>
    /// Whether the cancellation happened after acceptance but before pickup. Frozen at the moment
    /// of cancellation — the status it was derived from is overwritten by the same call.
    /// </summary>
    public bool CancelledBeforePickup { get; private set; }

    /// <summary>How often every candidate driver was exhausted without an accept.</summary>
    public int TimesUnassigned { get; private set; }

    public DateTime? LastUnassignedOnUtc { get; private set; }

    public Guid? DeliveryId { get; private set; }

    public Guid? DriverId { get; private set; }

    /// <summary>
    /// Drop-off coordinates, captured from OrderReadyForPickup — the only shipped event that
    /// carries them (OrderPlaced does not). Null for an order FraudDetection only ever saw placed, which is
    /// exactly the "no data ⇒ no signal" case Milestone D's location check must degrade into.
    /// </summary>
    public double? DropoffLatitude { get; private set; }

    public double? DropoffLongitude { get; private set; }

    public static OrderFact Create(
        Guid orderId,
        Guid customerId,
        Guid restaurantId,
        decimal subtotal,
        DateTime placedOnUtc)
    {
        return new OrderFact
        {
            Id = orderId,
            CustomerId = customerId,
            RestaurantId = restaurantId,
            Subtotal = subtotal,
            PlacedOnUtc = placedOnUtc,
            Status = OrderFactStatus.Placed
        };
    }

    public void MarkAccepted(DateTime acceptedOnUtc)
    {
        if (IsClosed)
        {
            return;
        }

        AcceptedOnUtc ??= acceptedOnUtc;
        Advance(OrderFactStatus.Accepted);
    }

    public void MarkReadyForPickup(DateTime readyOnUtc, double dropoffLatitude, double dropoffLongitude)
    {
        // The coordinates are recorded even on a closed order: they are reference data for the
        // location signals, not a state transition, and an order cancelled after being made ready
        // is precisely a case worth measuring.
        DropoffLatitude = dropoffLatitude;
        DropoffLongitude = dropoffLongitude;

        if (IsClosed)
        {
            return;
        }

        ReadyForPickupOnUtc ??= readyOnUtc;
        Advance(OrderFactStatus.ReadyForPickup);
    }

    public void MarkPickedUp(Guid deliveryId, Guid driverId, DateTime pickedUpOnUtc)
    {
        DeliveryId ??= deliveryId;
        DriverId ??= driverId;

        if (IsClosed)
        {
            return;
        }

        PickedUpOnUtc ??= pickedUpOnUtc;
        Advance(OrderFactStatus.PickedUp);
    }

    public void MarkDelivered(Guid deliveryId, Guid driverId, DateTime deliveredOnUtc)
    {
        DeliveryId ??= deliveryId;
        DriverId ??= driverId;

        if (IsClosed)
        {
            return;
        }

        DeliveredOnUtc = deliveredOnUtc;
        Status = OrderFactStatus.Delivered;
    }

    public void MarkCancelled(DateTime cancelledOnUtc)
    {
        if (IsClosed)
        {
            return;
        }

        // The promotion-abuse shape: accepted by the restaurant (so it cost them), cancelled before
        // a driver ever collected it. Read before Status is overwritten below.
        CancelledBeforePickup = Status is OrderFactStatus.Accepted or OrderFactStatus.ReadyForPickup;

        CancelledOnUtc = cancelledOnUtc;
        Status = OrderFactStatus.Cancelled;
    }

    public void MarkRejected(DateTime rejectedOnUtc)
    {
        if (IsClosed)
        {
            return;
        }

        RejectedOnUtc = rejectedOnUtc;
        Status = OrderFactStatus.Rejected;
    }

    /// <summary>
    /// Every candidate driver within the radius was tried without an accept. Not a status change —
    /// the order is still live and Delivery will re-offer it — so it is counted, not transitioned.
    /// </summary>
    public void RecordUnassigned(DateTime unassignedOnUtc)
    {
        if (IsClosed)
        {
            return;
        }

        TimesUnassigned++;
        LastUnassignedOnUtc = unassignedOnUtc;
    }

    /// <summary>
    /// The order has not reached a terminal state. Handlers gate their counter increments on this,
    /// so a redelivered "cancelled" for an order already recorded as cancelled cannot count twice.
    /// </summary>
    public bool IsOpen => !IsClosed;

    private bool IsClosed =>
        Status is OrderFactStatus.Delivered or OrderFactStatus.Cancelled or OrderFactStatus.Rejected;

    /// <summary>
    /// Moves forward only. Two events about the same order arrive on two queues, so a late
    /// OrderAccepted must not drag an order that is already out for delivery backwards.
    /// </summary>
    private void Advance(OrderFactStatus status)
    {
        if (status > Status)
        {
            Status = status;
        }
    }
}
