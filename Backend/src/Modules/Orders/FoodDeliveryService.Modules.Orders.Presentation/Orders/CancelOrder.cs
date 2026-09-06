using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Orders.Application;
using FoodDeliveryService.Modules.Orders.Application.Orders.CancelOrder;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Orders.Presentation.Orders;

// Customer-facing — gated on orders:create (the customer permission); the handler checks the caller
// owns the order and the domain enforces that a cancel is still allowed.
internal sealed class CancelOrder : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("orders/{id:guid}/cancel", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
        {
            Result result = await sender.Send(new CancelOrderCommand(id), cancellationToken);

            return result.Match(Results.NoContent, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.CreateOrder)
        .WithTags(Tags.Orders)
        .WithSummary("Cancel an order")
        .WithDescription(
            "Customer-facing, gated on the customer's own permission. The handler checks the caller " +
            "owns the order and the domain decides whether it is still early enough to cancel - a 409 " +
            "if it is not.")
        .Produces(StatusCodes.Status204NoContent);
    }
}
