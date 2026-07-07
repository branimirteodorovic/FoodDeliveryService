using System.Net;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Users.Application.Abstractions.Identity;
using Microsoft.Extensions.Logging;

namespace FoodDeliveryService.Modules.Users.Infrastructure.Identity;

internal sealed class IdentityProviderService(
    DuendeIdentityClient duendeIdentityClient,
    ILogger<IdentityProviderService> logger)
    : IIdentityProviderService
{
    // POST /api/users
    public async Task<Result<string>> RegisterUserAsync(UserModel user, CancellationToken cancellationToken = default)
    {
        var registerUserRequest = new RegisterUserRequest(
            user.Email,
            user.FirstName,
            user.LastName,
            user.Password);

        try
        {
            string identityId = await duendeIdentityClient.RegisterUserAsync(registerUserRequest, cancellationToken);

            return identityId;
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            logger.LogError(exception, "User registration failed");

            return Result.Failure<string>(IdentityProviderErrors.EmailIsNotUnique);
        }
    }

    // POST /api/users/invite
    public async Task<Result<InvitationResult>> RegisterInvitedUserAsync(
        InvitedUserModel user,
        CancellationToken cancellationToken = default)
    {
        var inviteUserRequest = new InviteUserRequest(user.Email, user.FirstName, user.LastName);

        try
        {
            InviteUserResponse response = await duendeIdentityClient.InviteUserAsync(inviteUserRequest, cancellationToken);

            return new InvitationResult(response.Id, response.ActivationToken, response.ExpiresOnUtc);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            logger.LogError(exception, "User invitation failed");

            return Result.Failure<InvitationResult>(IdentityProviderErrors.EmailIsNotUnique);
        }
    }

    // DELETE /api/users/{id}
    public async Task<Result> DeleteInvitedUserAsync(
        string identityId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await duendeIdentityClient.DeleteInvitedUserAsync(identityId, cancellationToken);

            return Result.Success();
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            // The invitation was accepted in the meantime — never delete an activated account.
            logger.LogError(exception, "Compensating removal of invited user {IdentityId} refused", identityId);

            return Result.Failure(IdentityProviderErrors.AccountAlreadyActivated);
        }
    }

    // POST /api/users/set-password
    public async Task<Result> SetPasswordAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var setPasswordRequest = new SetPasswordRequest(email, token, newPassword);

        try
        {
            await duendeIdentityClient.SetPasswordAsync(setPasswordRequest, cancellationToken);

            return Result.Success();
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.BadRequest)
        {
            // Invalid/expired token or an unknown email — surfaced as a clean failure, not a 500.
            logger.LogWarning(exception, "Setting password from invitation failed");

            return Result.Failure(IdentityProviderErrors.InvalidActivationToken);
        }
    }
}
