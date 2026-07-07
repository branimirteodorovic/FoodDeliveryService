using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Restaurants.Application;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetRestaurant;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetRestaurants;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Restaurants.Presentation.Restaurants;

internal sealed class GetRestaurants : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("restaurants", async (
            ISender sender,
            CancellationToken cancellationToken,
            int page = 1,
            int pageSize = 20) =>
        {
            Result<IReadOnlyCollection<RestaurantResponse>> result = await sender.Send(
                new GetRestaurantsQuery(page, pageSize),
                cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.GetRestaurants)
        .WithTags(Tags.Restaurants);
    }
}
