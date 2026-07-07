using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Restaurants.Application;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.UpdateMenuCategory;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Restaurants.Presentation.Restaurants;

// Rename / reorder a category. Nested under restaurants/** so it stays inside the module's
// existing YARP route prefix (no gateway change needed).
internal sealed class UpdateMenuCategory : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("restaurants/{restaurantId:guid}/menu-categories/{categoryId:guid}", async (
            Guid restaurantId,
            Guid categoryId,
            Request request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Result result = await sender.Send(
                new UpdateMenuCategoryCommand(restaurantId, categoryId, request.Name, request.DisplayOrder),
                cancellationToken);

            return result.Match(Results.NoContent, ApiResults.Problem);
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
