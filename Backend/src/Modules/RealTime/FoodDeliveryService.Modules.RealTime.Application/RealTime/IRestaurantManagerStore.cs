namespace FoodDeliveryService.Modules.RealTime.Application.RealTime;

/// <summary>
/// The service's first — and only — durable replica (Milestone D): <c>managerUserId → restaurant</c>,
/// upserted from Restaurants' <c>RestaurantRegisteredIntegrationEvent</c> /
/// <c>RestaurantAddressUpdatedIntegrationEvent</c> via the inbox (unlike every other consumer in this
/// module, this mapping must survive a cold start reliably, so it is NOT a direct, ephemeral
/// consumer — see the plan's §5.1 justification). Keyed by <c>managerUserId</c>
/// (<see cref="ConnectionClaims.GetUserId"/>'s "sub") because that is the only id
/// <c>TrackingHub.OnConnectedAsync</c> has to resolve a connecting manager's restaurant with — a
/// restaurant manager's <c>restaurantId</c> is never in their JWT.
/// </summary>
public interface IRestaurantManagerStore
{
    /// <summary>Upserts the manager→restaurant row from a full-snapshot Restaurants event.</summary>
    Task UpsertAsync(Guid managerUserId, Guid restaurantId, string restaurantName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes the restaurant name on every existing row for that restaurant — keyed by
    /// <paramref name="restaurantId"/> rather than the (unknown here) manager id, since
    /// <c>RestaurantAddressUpdatedIntegrationEvent</c> carries no <c>ManagerUserId</c>. A no-op if the
    /// restaurant was never registered here yet.
    /// </summary>
    Task UpdateRestaurantNameAsync(Guid restaurantId, string restaurantName, CancellationToken cancellationToken = default);

    /// <summary>
    /// The restaurant a manager is mapped to, or <c>null</c> if no replica row exists yet for them
    /// (a manager who registered before this service's inbox caught up — the connection simply joins
    /// no restaurant group; self-heals on the next reconnect once the row lands).
    /// </summary>
    Task<Guid?> GetRestaurantIdAsync(Guid managerUserId, CancellationToken cancellationToken = default);
}
