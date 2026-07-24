using FoodDeliveryService.Modules.RealTime.Application;
using FoodDeliveryService.Modules.RealTime.Application.RealTime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace FoodDeliveryService.Modules.RealTime.Presentation.Tracking;

/// <summary>
/// The single authenticated SignalR hub for live order and driver tracking. Every connection is
/// authenticated by the same Duende JWT (validated at the gateway and again here); on connect the
/// caller is placed in exactly the groups its own claims entitle it to — never a group derived from
/// a client-supplied id.
/// <para>
/// The socket is best-effort: a dropped frame is never a correctness problem. On connect and on
/// every SignalR auto-reconnect the client re-fetches authoritative state from the REST read models
/// (<c>GET orders/{id}</c>, <c>GET delivery/orders/{orderId}/delivery</c>) and then applies socket
/// deltas. That is the load-bearing assumption behind this service holding no durable per-frame
/// state and skipping the inbox for status fan-out.
/// </para>
/// </summary>
[Authorize]
internal sealed class TrackingHub(IRestaurantManagerStore restaurantManagerStore, ILogger<TrackingHub> logger) : Hub
{
    /// <summary>
    /// Joins the caller to every group its own claims entitle it to. Re-runs on every reconnect (the
    /// client uses <c>withAutomaticReconnect()</c>), so there is nothing to persist server-side.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        // From the resolved "sub" claim only — never from the client. Throws (aborting the
        // connection) if the principal carries no resolved user id.
        Guid userId = Context.User.GetUserId();

        // Every caller lands in their own group so a customer receives status/location frames for
        // their own orders.
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupNames.User(userId));

        // Milestone D: dashboard groups, derived from permission claims only — never a client-
        // supplied restaurant/support id (this service issues no role claim; see
        // RealTime.Application.Permissions for why a permission code doubles as the identity marker).
        if (Context.User.HasPermission(Permissions.RestaurantDashboard))
        {
            await TryJoinRestaurantGroupAsync(userId);
        }

        if (Context.User.HasPermission(Permissions.SupportDashboard))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupNames.Support);
        }

        await base.OnConnectedAsync();
    }

    private async Task TryJoinRestaurantGroupAsync(Guid userId)
    {
        Guid? restaurantId = await restaurantManagerStore.GetRestaurantIdAsync(userId);

        if (restaurantId is null)
        {
            // No replica row yet (e.g. an Administrator, who also holds this permission but manages
            // no restaurant of their own — or a manager whose registration event hasn't landed here
            // yet). Self-heals on the next reconnect once/if the row lands.
            logger.LogWarning(
                "Caller {UserId} has the restaurant-dashboard permission but no RestaurantManager replica row",
                userId);
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupNames.Restaurant(restaurantId.Value));
    }
}
