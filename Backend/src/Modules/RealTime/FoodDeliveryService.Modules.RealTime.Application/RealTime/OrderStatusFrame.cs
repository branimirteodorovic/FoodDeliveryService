using FoodDeliveryService.Modules.Orders.IntegrationEvents;

namespace FoodDeliveryService.Modules.RealTime.Application.RealTime;

/// <summary>
/// The server→client payload for the <c>OrderStatusChanged</c> hub method — a single frame on the
/// customer's live order-status timeline. This shape is part of the feature's public API; keep it
/// additive-only. <see cref="DriverName"/>/<see cref="DriverVehicle"/> are populated from Milestone C
/// (driver assignment) onward and are simply absent on the Orders-owned transitions covered here.
/// <para>
/// The frame is best-effort: the client re-syncs authoritative state from the REST read models on
/// (re)connect, so a dropped frame is never a correctness problem (see <c>TrackingHub</c>).
/// </para>
/// </summary>
public sealed record OrderStatusFrame(
    Guid OrderId,
    string Status,
    DateTime OccurredOnUtc,
    string? DriverName = null,
    string? DriverVehicle = null)
{
    // One pure mapping per Orders lifecycle event. OccurredOnUtc is the event's own transition
    // timestamp (the moment the domain event was raised), used uniformly so the client timeline is
    // ordered consistently across every status — including OrderReadyForPickup, which carries no
    // status-specific timestamp field of its own.
    public static OrderStatusFrame From(OrderPlacedIntegrationEvent integrationEvent) =>
        new(integrationEvent.OrderId, OrderStatuses.Placed, integrationEvent.OccurredOnUtc);

    public static OrderStatusFrame From(OrderAcceptedIntegrationEvent integrationEvent) =>
        new(integrationEvent.OrderId, OrderStatuses.Accepted, integrationEvent.OccurredOnUtc);

    public static OrderStatusFrame From(OrderRejectedIntegrationEvent integrationEvent) =>
        new(integrationEvent.OrderId, OrderStatuses.Rejected, integrationEvent.OccurredOnUtc);

    public static OrderStatusFrame From(OrderReadyForPickupIntegrationEvent integrationEvent) =>
        new(integrationEvent.OrderId, OrderStatuses.ReadyForPickup, integrationEvent.OccurredOnUtc);

    public static OrderStatusFrame From(OrderCancelledIntegrationEvent integrationEvent) =>
        new(integrationEvent.OrderId, OrderStatuses.Cancelled, integrationEvent.OccurredOnUtc);
}
