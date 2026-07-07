using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Provisioning;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MassTransit;

namespace FoodDeliveryService.Modules.Restaurants.Infrastructure.Provisioning;

/// <summary>
/// MassTransit request/response implementation of the onboarding calls to the Users service
/// (same mechanism as PermissionService → GetUserPermissionsRequest). The consumers respond with
/// either the success payload or an <see cref="Error"/>, so duplicate-email/validation failures
/// surface as proper Result failures instead of timeouts or 500s.
/// </summary>
internal sealed class ManagerProvisioningService(
    IRequestClient<ProvisionManagerUserRequest> provisionClient,
    IRequestClient<DeactivateProvisionedUserRequest> deactivateClient) : IManagerProvisioningService
{
    private static readonly Error ProvisioningFailed = Error.Failure(
        "Restaurants.ManagerProvisioningFailed",
        "Provisioning the restaurant manager account failed.");

    public async Task<Result<Guid>> ProvisionManagerAsync(
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken = default)
    {
        var response = await provisionClient.GetResponse<ProvisionManagerUserResponse, Error>(
            new ProvisionManagerUserRequest(email, firstName, lastName),
            cancellationToken);

        if (response.Is(out Response<Error> errorResponse))
        {
            return Result.Failure<Guid>(errorResponse.Message);
        }

        if (response.Is(out Response<ProvisionManagerUserResponse> provisionResponse))
        {
            return provisionResponse.Message.UserId;
        }

        return Result.Failure<Guid>(ProvisioningFailed);
    }

    public async Task<Result> DeactivateManagerAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var response = await deactivateClient.GetResponse<DeactivateProvisionedUserResponse, Error>(
            new DeactivateProvisionedUserRequest(userId),
            cancellationToken);

        if (response.Is(out Response<Error> errorResponse))
        {
            return Result.Failure(errorResponse.Message);
        }

        return Result.Success();
    }
}
