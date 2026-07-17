using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Delivery.Application;
using FoodDeliveryService.Modules.Delivery.Application.Drivers.GetDriver;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Delivery.Presentation.Drivers;

/// <summary>
/// Driver profile by id — self or admin (the query handler enforces the self-or-admin check).
/// </summary>
internal sealed class GetDriver : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("delivery/drivers/{id:guid}", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
        {
            Result<DriverResponse> result = await sender.Send(new GetDriverQuery(id), cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.GetDrivers)
        .WithTags(Tags.Drivers);
    }
}
