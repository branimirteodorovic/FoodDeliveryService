using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Restaurants.Application;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.SetMenuItemAvailability;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Restaurants.Presentation.Restaurants;

// Mark a menu item available / sold out.
internal sealed class SetMenuItemAvailability : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("restaurants/{restaurantId:guid}/menu-items/{menuItemId:guid}/availability", async (
            Guid restaurantId,
            Guid menuItemId,
            Request request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Result result = await sender.Send(
                new SetMenuItemAvailabilityCommand(restaurantId, menuItemId, request.IsAvailable),
                cancellationToken);

            return result.Match(Results.NoContent, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.ManageMenu)
        .WithTags(Tags.Restaurants)
        .WithSummary("Set menu item availability")
        .WithDescription(
            "Marks an item available or sold out - the fast path a kitchen uses mid-service, without " +
            "touching the rest of the item.")
        .Produces(StatusCodes.Status204NoContent);
    }

    internal sealed class Request
    {
        public bool IsAvailable { get; init; }
    }
}
