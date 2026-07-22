namespace FoodDeliveryService.Modules.RealTime.Application.RealTime;

/// <summary>
/// The stable, client-facing order-status strings pushed on <see cref="OrderStatusFrame.Status"/>.
/// They are intentionally decoupled from any service's internal enum: the socket contract is the
/// public API of this feature, so these values must stay stable and additive-only after they land.
/// Milestone B covers the Orders-owned transitions; the two "final" statuses (out-for-delivery,
/// delivered) arrive in Milestone C, reconstructed from Delivery's own events.
/// </summary>
public static class OrderStatuses
{
    public const string Placed = "Placed";

    public const string Accepted = "Accepted";

    public const string Rejected = "Rejected";

    public const string ReadyForPickup = "ReadyForPickup";

    public const string Cancelled = "Cancelled";
}
