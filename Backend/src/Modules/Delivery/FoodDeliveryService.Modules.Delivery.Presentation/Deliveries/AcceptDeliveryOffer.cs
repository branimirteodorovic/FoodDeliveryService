using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Delivery.Application;
using FoodDeliveryService.Modules.Delivery.Application.Deliveries.AcceptDeliveryOffer;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Delivery.Presentation.Deliveries;

/// <summary>
/// The offered driver accepts the delivery. Ownership is enforced in the domain — the command
/// targets the authenticated caller, and only the driver the offer went to can accept it.
/// </summary>
internal sealed class AcceptDeliveryOffer : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("delivery/deliveries/{id:guid}/accept", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
        {
            Result result = await sender.Send(new AcceptDeliveryOfferCommand(id), cancellationToken);

            return result.Match(Results.NoContent, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.ManageDeliveries)
        .WithTags(Tags.Deliveries);
    }
}
