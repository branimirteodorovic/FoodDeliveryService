using FoodDeliveryService.Common.Application.Caching;

namespace FoodDeliveryService.Modules.Delivery.Application.Abstractions.Assignment;

/// <summary>
/// The two resources the assignment path serializes on, built through the shared key convention so
/// the offer routine and the accept handler can never drift onto different names for the same lock.
/// </summary>
public static class DeliveryLocks
{
    /// <summary>
    /// How long an acquisition survives without an explicit release. Long enough to cover the
    /// critical section (a geo search plus one database transaction) with room for a slow round
    /// trip, and far short of the 30 s offer window so a crashed holder cannot block assignment for
    /// anything close to the life of an offer.
    /// </summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Held while a driver is being selected for an offer or is reserving themselves by accepting
    /// one — the lock that stops the same driver being handed two orders at once.
    /// </summary>
    public static string Driver(Guid driverId) => CacheKeys.Create("delivery", "driver-lock", driverId);

    /// <summary>
    /// Held for the whole offer routine of one delivery, so its overlapping triggers (a rejection
    /// re-offer, the expiry job tick, a fresh create) run one at a time instead of double-offering.
    /// </summary>
    public static string Offer(Guid deliveryId) => CacheKeys.Create("delivery", "offer-lock", deliveryId);
}
