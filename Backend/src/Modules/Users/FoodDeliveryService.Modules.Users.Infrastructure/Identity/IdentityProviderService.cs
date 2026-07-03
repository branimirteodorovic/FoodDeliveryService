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
}
