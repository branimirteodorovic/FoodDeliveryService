using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Users.Application.Users.RegisterUser;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Users.Presentation.Users;

/// <summary>
/// Customer self-registration — the ONLY anonymous account-creation path. The role is forced to
/// Customer here; staff/partner accounts (RestaurantManager, …) are admin-provisioned via the
/// ProvisionManagerUserRequest RPC and activated by invitation, never through this endpoint.
/// </summary>
internal sealed class RegisterUser : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("users/register", async (Request request, ISender sender, CancellationToken cancellationToken) =>
        {
            var command = new RegisterUserCommand(
                request.Email,
                request.Password,
                request.FirstName,
                request.LastName);

            Result<Guid> result = await sender.Send(command, cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .AllowAnonymous()
        .WithTags(Tags.Users)
        .WithSummary("Register a customer")
        .WithDescription(
            "Self-registration, and one of only two anonymous endpoints on the platform - a caller " +
            "who has no account cannot hold a token for one. The handler forces the role to Customer " +
            "regardless of what is sent. Provisions the credential in Duende first, then the module- " +
            "side user.")
        .Produces<Guid>();
    }

    internal sealed class Request
    {
        public string Email { get; init; }

        public string Password { get; init; }

        public string FirstName { get; init; }

        public string LastName { get; init; }
    }
}
