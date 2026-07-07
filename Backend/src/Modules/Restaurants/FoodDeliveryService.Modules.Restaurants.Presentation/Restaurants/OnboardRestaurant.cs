using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Restaurants.Application;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.OnboardRestaurant;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Restaurants.Presentation.Restaurants;

/// <summary>
/// Single admin action that onboards a restaurant: business fields (incl. commission rate) plus
/// the manager's contact details. Administrator only — restaurant managers are provisioned, they
/// never self-register.
/// </summary>
internal sealed class OnboardRestaurant : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("restaurants", async (Request request, ISender sender, CancellationToken cancellationToken) =>
        {
            var command = new OnboardRestaurantCommand(
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
                request.Longitude,
                request.CommissionRate,
                request.ManagerEmail,
                request.ManagerFirstName,
                request.ManagerLastName);

            Result<Guid> result = await sender.Send(command, cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.CreateRestaurant)
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

        // Fraction in [0, 1) — e.g. 0.20 = 20%.
        public decimal CommissionRate { get; init; }

        public string ManagerEmail { get; init; }

        public string ManagerFirstName { get; init; }

        public string ManagerLastName { get; init; }
    }
}
