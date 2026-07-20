using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

namespace FoodDeliveryService.Modules.RealTime.UnitTests.RealTime.Fakes;

/// <summary>
/// Minimal <see cref="HubCallerContext"/> test double carrying a fixed connection id and principal,
/// so the hub's <c>OnConnectedAsync</c> can be exercised without a running SignalR pipeline.
/// </summary>
internal sealed class FakeHubCallerContext(string connectionId, ClaimsPrincipal? user) : HubCallerContext
{
    public override string ConnectionId { get; } = connectionId;

    public override string? UserIdentifier => User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public override ClaimsPrincipal? User { get; } = user;

    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

    public override IFeatureCollection Features { get; } = new FeatureCollection();

    public override CancellationToken ConnectionAborted => CancellationToken.None;

    public override void Abort()
    {
        // No-op: the tests never abort the connection.
    }
}
