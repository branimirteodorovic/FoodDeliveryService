using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Delivery.Application;
using FoodDeliveryService.Modules.Delivery.Application.Deliveries.MarkDeliveryPickedUp;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Delivery.Presentation.Deliveries;

/// <summary>
/// The assigned driver marks the food collected. Ownership is enforced in the domain — only the
/// driver the delivery is assigned to can pick it up.
/// </summary>
internal sealed class MarkDeliveryPickedUp : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("delivery/deliveries/{id:guid}/picked-up", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
        {
            Result result = await sender.Send(new MarkDeliveryPickedUpCommand(id), cancellationToken);

            return result.Match(Results.NoContent, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.ManageDeliveries)
        .WithTags(Tags.Deliveries);
    }
}
