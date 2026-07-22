namespace FoodDeliveryService.Modules.RealTime.Application.RealTime;

/// <summary>
/// The server→client method names on the tracking hub, centralised so the fan-out implementation,
/// the hub's documented contract and the tests all agree on one spelling. These are part of the
/// feature's public API — additive-only once shipped.
/// </summary>
public static class TrackingHubMethods
{
    /// <summary>A frame on the customer's live order-status timeline (Milestone B onward).</summary>
    public const string OrderStatusChanged = "OrderStatusChanged";

    /// <summary>A driver-position update for the customer's tracking pin (Milestone C onward).</summary>
    public const string DriverLocationChanged = "DriverLocationChanged";
}
