using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Delivery.Application;
using FoodDeliveryService.Modules.Delivery.Application.Drivers.OnboardDriver;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Delivery.Presentation.Drivers;

/// <summary>
/// Single admin action that onboards a driver: contact details + vehicle. Administrator only —
/// drivers are provisioned (invited account in Users, activation email), they never self-register.
/// Returns the driver id (= the provisioned UserId).
/// </summary>
internal sealed class OnboardDriver : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("delivery/drivers", async (Request request, ISender sender, CancellationToken cancellationToken) =>
        {
            var command = new OnboardDriverCommand(
                request.Email,
                request.FirstName,
                request.LastName,
                request.VehicleType);

            Result<Guid> result = await sender.Send(command, cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.ProvisionUsers)
        .WithTags(Tags.Drivers);
    }

    internal sealed class Request
    {
        public string Email { get; init; }

        public string FirstName { get; init; }

        public string LastName { get; init; }

        // Enum name: Bicycle | Motorcycle | Car.
        public string VehicleType { get; init; }
    }
}
