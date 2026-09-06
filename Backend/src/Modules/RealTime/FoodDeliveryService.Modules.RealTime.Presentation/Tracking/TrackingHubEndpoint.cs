using FoodDeliveryService.Common.Presentation.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
        app.MapHub<TrackingHub>("hubs/tracking")
            .RequireAuthorization()
            // Metadata for people, not for the OpenAPI document: MapHub contributes no
            // ApiDescription, so a hub never appears in a generated document however it is
            // annotated (OpenApiDocumentTests records that). WithTags is RouteHandlerBuilder-only
            // for the same underlying reason — a hub's builder is not one.
            .WithMetadata(new TagsAttribute(Tags.Tracking))
            .WithSummary("Live order and delivery tracking (SignalR)")
            .WithDescription(
                "A SignalR hub, not an HTTP endpoint: connect with a SignalR client rather than " +
                "calling this from the reference UI. The handshake requires a bearer token but no " +
                "particular permission - the hub derives the caller's groups from their permission " +
                "claims after connecting, because customers, drivers, managers and agents all " +
                "legitimately connect and each is shown a different slice.");
    }
}
