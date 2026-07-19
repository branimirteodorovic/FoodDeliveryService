using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Delivery.Application;
using FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetDelivery;
using FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetDeliveryByOrder;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Delivery.Presentation.Deliveries;

/// <summary>
/// The customer's tracking lookup by order id — Feature 2.2 renders this. The order's customer, the
/// assigned driver, or an admin may read it (enforced in the query handler).
/// </summary>
internal sealed class GetDeliveryByOrder : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("delivery/orders/{orderId:guid}/delivery", async (Guid orderId, ISender sender, CancellationToken cancellationToken) =>
        {
            Result<DeliveryResponse> result = await sender.Send(new GetDeliveryByOrderQuery(orderId), cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.GetDeliveries)
        .WithTags(Tags.Deliveries);
    }
}
