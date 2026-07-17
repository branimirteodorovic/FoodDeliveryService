using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Delivery.Application;
using FoodDeliveryService.Modules.Delivery.Application.Drivers.UpdateDriverProfile;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Delivery.Presentation.Drivers;

/// <summary>
/// The driver edits their own name/vehicle. Self only by construction — the command targets the
/// authenticated caller, so there is no id to tamper with.
/// </summary>
internal sealed class UpdateMyDriverProfile : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("delivery/drivers/me", async (Request request, ISender sender, CancellationToken cancellationToken) =>
        {
            var command = new UpdateDriverProfileCommand(
                request.FirstName,
                request.LastName,
                request.VehicleType);

            Result result = await sender.Send(command, cancellationToken);

            return result.Match(Results.NoContent, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.ModifyDriver)
        .WithTags(Tags.Drivers);
    }

    internal sealed class Request
    {
        public string FirstName { get; init; }

        public string LastName { get; init; }

        // Enum name: Bicycle | Motorcycle | Car.
        public string VehicleType { get; init; }
    }
}
