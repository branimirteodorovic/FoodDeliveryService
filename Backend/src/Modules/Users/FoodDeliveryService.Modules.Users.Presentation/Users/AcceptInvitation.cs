using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Users.Application.Users.AcceptInvitation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Users.Presentation.Users;

/// <summary>
/// Activates an invited account: the invitee supplies the one-time token from their invitation
/// email and chooses a password. Anonymous — the account has no session/credentials yet. An
/// invalid or expired token is rejected as a clean 400.
/// </summary>
internal sealed class AcceptInvitation : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("users/accept-invitation", async (Request request, ISender sender, CancellationToken cancellationToken) =>
        {
            var command = new AcceptInvitationCommand(request.Email, request.Token, request.NewPassword);

            Result result = await sender.Send(command, cancellationToken);

            return result.Match(Results.NoContent, ApiResults.Problem);
        })
        .AllowAnonymous()
        .WithTags("Users");
    }

    internal sealed class Request
    {
        public string Email { get; init; }

        public string Token { get; init; }

        public string NewPassword { get; init; }
    }
}
