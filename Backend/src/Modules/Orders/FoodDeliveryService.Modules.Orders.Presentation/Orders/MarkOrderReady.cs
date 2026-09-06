using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Orders.Application;
using FoodDeliveryService.Modules.Orders.Application.Orders.MarkOrderReady;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Orders.Presentation.Orders;

internal sealed class MarkOrderReady : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("orders/{id:guid}/ready", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
        {
            Result result = await sender.Send(new MarkOrderReadyCommand(id), cancellationToken);

            return result.Match(Results.NoContent, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.ManageOrders)
        .WithTags(Tags.Orders)
        .WithSummary("Mark an order ready")
        .WithDescription(
            "Preparing -> ReadyForPickup, which is what makes the order eligible for a driver offer " +
            "in the delivery service.")
        .Produces(StatusCodes.Status204NoContent);
    }
}
