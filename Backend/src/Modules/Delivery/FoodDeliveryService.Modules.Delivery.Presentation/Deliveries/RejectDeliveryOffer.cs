using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Delivery.Application;
using FoodDeliveryService.Modules.Delivery.Application.Deliveries.RejectDeliveryOffer;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Delivery.Presentation.Deliveries;

/// <summary>
/// The offered driver declines the delivery; the offer routine immediately falls through to the
/// next-nearest candidate (or parks the delivery as Unassigned when none remain).
/// </summary>
internal sealed class RejectDeliveryOffer : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("delivery/deliveries/{id:guid}/reject", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
        {
            Result result = await sender.Send(new RejectDeliveryOfferCommand(id), cancellationToken);

            return result.Match(Results.NoContent, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.ManageDeliveries)
        .WithTags(Tags.Deliveries);
    }
}
