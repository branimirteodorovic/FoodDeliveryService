using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Restaurants.Application;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.UpdateMenuItem;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Restaurants.Presentation.Restaurants;

internal sealed class UpdateMenuItem : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("restaurants/{restaurantId:guid}/menu-items/{menuItemId:guid}", async (
            Guid restaurantId,
            Guid menuItemId,
            Request request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var command = new UpdateMenuItemCommand(
                restaurantId,
                menuItemId,
                request.Name,
                request.Description,
                request.Price,
                request.PhotoUrl);

            Result result = await sender.Send(command, cancellationToken);

            return result.Match(Results.NoContent, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.ManageMenu)
        .WithTags(Tags.Restaurants);
    }

    internal sealed class Request
    {
        public string Name { get; init; }

        public string Description { get; init; } = string.Empty;

        public decimal Price { get; init; }

        public string? PhotoUrl { get; init; }
    }
}
