using FoodDeliveryService.Modules.RealTime.Application.RealTime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

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
internal sealed class TrackingHub : Hub
{
    /// <summary>
    /// Joins the caller to their own <c>user:{sub}</c> group so a customer receives status and
    /// location frames for their own orders. Re-runs on every reconnect (the client uses
    /// <c>withAutomaticReconnect()</c>), so there is nothing to persist server-side.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        // From the resolved "sub" claim only — never from the client. Throws (aborting the
        // connection) if the principal carries no resolved user id.
        Guid userId = Context.User.GetUserId();

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupNames.User(userId));

        await base.OnConnectedAsync();
    }
}
