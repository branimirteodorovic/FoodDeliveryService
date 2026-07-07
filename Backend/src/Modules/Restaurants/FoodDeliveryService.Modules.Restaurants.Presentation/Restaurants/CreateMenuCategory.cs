using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Restaurants.Application;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.CreateMenuCategory;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Restaurants.Presentation.Restaurants;

internal sealed class CreateMenuCategory : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("restaurants/{restaurantId:guid}/menu-categories", async (
            Guid restaurantId,
            Request request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Result<Guid> result = await sender.Send(
                new CreateMenuCategoryCommand(restaurantId, request.Name, request.DisplayOrder),
                cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.ManageMenu)
        .WithTags(Tags.Restaurants);
    }

    internal sealed class Request
    {
        public string Name { get; init; }

        public int DisplayOrder { get; init; }
    }
}
