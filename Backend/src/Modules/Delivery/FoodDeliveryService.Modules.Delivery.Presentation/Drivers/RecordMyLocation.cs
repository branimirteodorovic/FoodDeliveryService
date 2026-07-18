using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Delivery.Application;
using FoodDeliveryService.Modules.Delivery.Application.Drivers.RecordDriverLocation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Delivery.Presentation.Drivers;

/// <summary>
/// The driver app's position report — the system's highest-traffic endpoint, called every few
/// seconds per active driver. Self only. Bypasses the aggregate and the outbox by design (see the
/// command handler): a position is telemetry, not domain state. Rate limiting at the gateway needs
/// a higher bucket for this path (Feature 1.3).
/// </summary>
internal sealed class RecordMyLocation : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("delivery/drivers/me/location", async (Request request, ISender sender, CancellationToken cancellationToken) =>
        {
            var command = new RecordDriverLocationCommand(request.Latitude, request.Longitude);

            Result result = await sender.Send(command, cancellationToken);

            return result.Match(Results.NoContent, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.ModifyDriver)
        .WithTags(Tags.Drivers);
    }

    internal sealed class Request
    {
        public double Latitude { get; init; }

        public double Longitude { get; init; }
    }
}
