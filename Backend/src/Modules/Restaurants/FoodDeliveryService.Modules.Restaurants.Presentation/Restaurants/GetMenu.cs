using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Restaurants.Application;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetMenu;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Restaurants.Presentation.Restaurants;

// Full menu (categories + items) for the storefront.
internal sealed class GetMenu : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("restaurants/{restaurantId:guid}/menu", async (
            Guid restaurantId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Result<MenuResponse> result = await sender.Send(new GetMenuQuery(restaurantId), cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.GetMenu)
        .WithTags(Tags.Restaurants);
    }
}
