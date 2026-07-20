using FoodDeliveryService.Common.Presentation.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.RealTime.Presentation.Tracking;

/// <summary>
/// Self-registers the tracking hub at <c>hubs/tracking</c> through the same <see cref="IEndpoint"/>
/// discovery every module uses, so the host maps it via <c>app.MapEndpoints()</c> with no manual
/// route table. <c>RequireAuthorization()</c> rejects an unauthenticated handshake at the negotiate
/// step. YARP forwards the WebSocket upgrade on the gateway's authenticated <c>hubs/**</c> route.
/// </summary>
internal sealed class TrackingHubEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapHub<TrackingHub>("hubs/tracking").RequireAuthorization();
    }
}
