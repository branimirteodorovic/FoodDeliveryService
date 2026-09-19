using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Delivery.Application;
using FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetMyDeliveryOffers;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Delivery.Presentation.Drivers;

/// <summary>
/// The driver's offer screen. Sits in the self-scoped <c>delivery/drivers/me/*</c> family — like
/// the profile, availability and location routes — and carries the same <c>drivers:read</c>
/// permission those reads do: it is the narrower of the two candidates (customers hold
/// <c>deliveries:read</c>, only drivers and administrators hold <c>drivers:read</c>) and it is
/// already what "my own driver state" is spelled as here.
/// </summary>
internal sealed class GetMyDeliveryOffers : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("delivery/drivers/me/offers", async (ISender sender, CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyCollection<DeliveryOfferResponse>> result =
                await sender.Send(new GetMyDeliveryOffersQuery(), cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.GetDrivers)
        .WithTags(Tags.Drivers)
        .WithSummary("List the calling driver's outstanding delivery offers")
        .WithDescription(
            "Every delivery currently offered to the caller and not yet lapsed, soonest deadline " +
            "first, with the pickup and drop-off detail the offer screen needs to decide. The " +
            "driver is resolved from the token, never from a parameter. Offers already past " +
            "OfferExpiresOnUtc are excluded server-side, so an empty list means there is nothing " +
            "left to accept. Accept or decline one with POST delivery/deliveries/{id}/accept " +
            "or /reject.")
        .Produces<IReadOnlyCollection<DeliveryOfferResponse>>();
    }
}
