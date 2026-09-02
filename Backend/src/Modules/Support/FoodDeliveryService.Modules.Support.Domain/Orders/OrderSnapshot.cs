using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Orders;

/// <summary>
/// Local read-only replica of an order, keyed by the Orders service's OrderId and built from the
/// lifecycle integration events Orders and Delivery already publish. Support may not query another
/// service's tables (hard rule #5), and every one of those events carries a full snapshot (hard
/// rule #9), so a projection is the only way this module can know what an order cost.
/// <para>
/// <strong>Partial by design, and this is the seam the context milestone extends.</strong> The
/// refund workflow needs exactly one fact about an order — its subtotal, which is the ceiling on
/// what may be refunded — so only <c>OrderPlacedIntegrationEvent</c> is projected here. The status,
/// the rejection reason, the delivery address, the driver and the <c>OrderTimelineEntry</c> table
/// arrive with the remaining seven events; adding them is a column-and-handler change on this type,
/// not a rewrite, which is why <see cref="LastEventOnUtc"/> already exists.
/// </para>
/// <para>
/// As a projection of state another service owns it raises no domain events and carries no business
/// rules: Orders published the originating ones, and re-deciding anything here would be a second,
/// divergent copy of a lifecycle this module does not own.
/// </para>
/// </summary>
public sealed class OrderSnapshot : Entity
{
    private OrderSnapshot()
    {
    }

    /// <summary>The Orders service's own OrderId, carried in on the event — never generated here.</summary>
    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid RestaurantId { get; private set; }

    /// <summary>
    /// What the customer was charged for the items, and therefore the most that can ever be
    /// refunded against this order. The single reason this replica exists today.
    /// </summary>
    public decimal Subtotal { get; private set; }

    public DateTime PlacedOnUtc { get; private set; }

    /// <summary>
    /// The <c>OccurredOnUtc</c> of the most advanced event applied to this row.
    /// <para>
    /// Nothing guarantees the order the broker delivers in — a redelivered <c>OrderPlaced</c> can
    /// arrive after later events — so every future projection compares against this before writing
    /// rather than blindly assigning, which is what keeps a late-arriving earlier event from
    /// regressing a more advanced status. It is recorded from the first event so that rule has a
    /// value to compare against on the day it is needed.
    /// </para>
    /// </summary>
    public DateTime LastEventOnUtc { get; private set; }

    public static OrderSnapshot Create(
        Guid orderId,
        Guid customerId,
        Guid restaurantId,
        decimal subtotal,
        DateTime placedOnUtc,
        DateTime occurredOnUtc)
    {
        return new OrderSnapshot
        {
            Id = orderId,
            CustomerId = customerId,
            RestaurantId = restaurantId,
            Subtotal = subtotal,
            PlacedOnUtc = placedOnUtc,
            LastEventOnUtc = occurredOnUtc
        };
    }

    /// <summary>
    /// Re-applies an <c>OrderPlaced</c> event to a row that already exists — an upsert rather than
    /// an insert. The inbox already dedupes on message id, but the projection must tolerate replay
    /// on its own: a row rebuilt from scratch, or a second delivery through a different path, must
    /// not fail and must not produce a different answer.
    /// </summary>
    public void ApplyPlaced(
        Guid customerId,
        Guid restaurantId,
        decimal subtotal,
        DateTime placedOnUtc,
        DateTime occurredOnUtc)
    {
        CustomerId = customerId;
        RestaurantId = restaurantId;
        Subtotal = subtotal;
        PlacedOnUtc = placedOnUtc;

        // Never moves backwards: a redelivered OrderPlaced arriving after a later event must not
        // make this row look less advanced than it is.
        if (occurredOnUtc > LastEventOnUtc)
        {
            LastEventOnUtc = occurredOnUtc;
        }
    }
}
