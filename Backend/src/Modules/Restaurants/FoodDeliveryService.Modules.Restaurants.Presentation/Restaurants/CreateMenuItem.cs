using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Restaurants.Application;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.CreateMenuItem;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Restaurants.Presentation.Restaurants;

internal sealed class CreateMenuItem : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("restaurants/{restaurantId:guid}/menu-items", async (
            Guid restaurantId,
            Request request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var command = new CreateMenuItemCommand(
                restaurantId,
                request.CategoryId,
                request.Name,
                request.Description,
                request.Price,
                request.PhotoUrl,
                request.IsAvailable);

            Result<Guid> result = await sender.Send(command, cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.ManageMenu)
        .WithTags(Tags.Restaurants);
    }

    internal sealed class Request
    {
        public Guid CategoryId { get; init; }

        public string Name { get; init; }

        public string Description { get; init; } = string.Empty;

        public decimal Price { get; init; }

        // URL only — the upload/photography flow is out of scope this phase.
        public string? PhotoUrl { get; init; }

        public bool IsAvailable { get; init; } = true;
    }
}
