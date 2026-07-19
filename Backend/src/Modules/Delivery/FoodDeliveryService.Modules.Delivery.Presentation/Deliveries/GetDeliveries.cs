using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Delivery.Application;
using FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetDeliveries;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Delivery.Presentation.Deliveries;

// The caller's delivery history — a driver's own, all for an admin. Scope is resolved in the
// handler from the authenticated identity, not a query parameter.
internal sealed class GetDeliveries : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("delivery/deliveries", async (
            int? page,
            int? pageSize,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyCollection<DeliverySummaryResponse>> result = await sender.Send(
                new GetDeliveriesQuery(page ?? 1, pageSize ?? 20),
                cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.GetDeliveries)
        .WithTags(Tags.Deliveries);
    }
}
