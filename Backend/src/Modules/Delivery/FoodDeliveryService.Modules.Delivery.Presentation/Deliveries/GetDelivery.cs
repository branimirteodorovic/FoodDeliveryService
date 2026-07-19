using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Delivery.Application;
using FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetDelivery;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Delivery.Presentation.Deliveries;

/// <summary>
/// A single delivery with the driver's name and current position — the assigned driver, the order's
/// customer, or an admin (the query handler enforces the read-guard).
/// </summary>
internal sealed class GetDelivery : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("delivery/deliveries/{id:guid}", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
        {
            Result<DeliveryResponse> result = await sender.Send(new GetDeliveryQuery(id), cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.GetDeliveries)
        .WithTags(Tags.Deliveries);
    }
}
