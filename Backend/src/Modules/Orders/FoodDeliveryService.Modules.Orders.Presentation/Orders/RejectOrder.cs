using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Orders.Application;
using FoodDeliveryService.Modules.Orders.Application.Orders.RejectOrder;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Orders.Presentation.Orders;

internal sealed class RejectOrder : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("orders/{id:guid}/reject", async (
            Guid id,
            Request request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Result result = await sender.Send(new RejectOrderCommand(id, request.Reason), cancellationToken);

            return result.Match(Results.NoContent, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.ManageOrders)
        .WithTags(Tags.Orders)
        .WithSummary("Reject an order")
        .WithDescription(
            "The restaurant declines the order, with a reason the customer sees. Terminal - a " +
            "rejected order cannot re-enter the lifecycle.")
        .Produces(StatusCodes.Status204NoContent);
    }

    internal sealed class Request
    {
        public string Reason { get; init; } = string.Empty;
    }
}
