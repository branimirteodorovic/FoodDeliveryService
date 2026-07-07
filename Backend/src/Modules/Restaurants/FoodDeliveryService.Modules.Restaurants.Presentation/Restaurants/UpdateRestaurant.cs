using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Restaurants.Application;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.UpdateRestaurant;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Restaurants.Presentation.Restaurants;

// Ownership-checked in the handler: only the owning manager (or an administrator) may update.
internal sealed class UpdateRestaurant : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("restaurants/{id:guid}", async (
            Guid id,
            Request request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var command = new UpdateRestaurantCommand(
                id,
                request.Name,
                request.TaxIdentification,
                request.CuisineType,
                request.Email,
                request.PhoneNumber,
                request.Street,
                request.City,
                request.PostalCode,
                request.Country,
                request.Latitude,
                request.Longitude);

            Result result = await sender.Send(command, cancellationToken);

            return result.Match(Results.NoContent, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.ModifyRestaurant)
        .WithTags(Tags.Restaurants);
    }

    internal sealed class Request
    {
        public string Name { get; init; }

        public string TaxIdentification { get; init; }

        public string CuisineType { get; init; }

        public string Email { get; init; }

        public string PhoneNumber { get; init; }

        public string Street { get; init; }

        public string City { get; init; }

        public string PostalCode { get; init; }

        public string Country { get; init; }

        public double? Latitude { get; init; }

        public double? Longitude { get; init; }
    }
}
