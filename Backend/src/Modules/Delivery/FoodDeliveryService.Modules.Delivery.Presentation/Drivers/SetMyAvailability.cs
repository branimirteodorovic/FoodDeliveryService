using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Delivery.Application;
using FoodDeliveryService.Modules.Delivery.Application.Drivers.SetDriverAvailability;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Delivery.Presentation.Drivers;

/// <summary>
/// The driver clocks on or off. Self only by construction — the command targets the authenticated
/// caller, so there is no id to tamper with.
/// </summary>
internal sealed class SetMyAvailability : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("delivery/drivers/me/availability", async (Request request, ISender sender, CancellationToken cancellationToken) =>
        {
            Result result = await sender.Send(new SetDriverAvailabilityCommand(request.Available), cancellationToken);

            return result.Match(Results.NoContent, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.ModifyDriver)
        .WithTags(Tags.Drivers);
    }

    internal sealed class Request
    {
        public bool Available { get; init; }
    }
}
