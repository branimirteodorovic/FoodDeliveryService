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
/// Convenience route for the caller's own profile — a null DriverId makes the query handler
/// resolve the authenticated user's id.
/// </summary>
internal sealed class GetMyDriverProfile : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("delivery/drivers/me", async (ISender sender, CancellationToken cancellationToken) =>
        {
            Result<DriverResponse> result = await sender.Send(new GetDriverQuery(null), cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.GetDrivers)
        .WithTags(Tags.Drivers);
    }
}
