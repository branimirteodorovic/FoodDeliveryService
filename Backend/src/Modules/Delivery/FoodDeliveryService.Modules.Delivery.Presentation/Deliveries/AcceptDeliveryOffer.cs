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
        .WithTags(Tags.Deliveries)
        .WithSummary("Accept a delivery offer")
        .WithDescription(
            "Takes an offer that is still open. Guarded by a distributed lock as well as by the " +
            "aggregate: two drivers accepting the same offer in the same instant is exactly the race " +
            "no concurrency token here would catch. A lost race is a 409.")
        .Produces(StatusCodes.Status204NoContent);
    }
}
