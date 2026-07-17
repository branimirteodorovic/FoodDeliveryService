using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Provisioning;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MassTransit;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Provisioning;

/// <summary>
/// MassTransit request/response implementation of the onboarding calls to the Users service, via
/// the generalized <see cref="ProvisionUserRequest"/> contract (role carried on the request — no
/// per-role sibling contract). The consumers respond with either the success payload or an
/// <see cref="Error"/>, so duplicate-email/validation failures surface as proper Result failures
/// instead of timeouts or 500s.
/// </summary>
internal sealed class DriverProvisioningService(
    IRequestClient<ProvisionUserRequest> provisionClient,
    IRequestClient<DeactivateProvisionedUserRequest> deactivateClient) : IDriverProvisioningService
{
    // Must match Users.Domain Role.DeliveryDriver's name; validated by ProvisionUserRequestConsumer.
    private const string DeliveryDriverRole = "DeliveryDriver";

    private static readonly Error ProvisioningFailed = Error.Failure(
        "Delivery.DriverProvisioningFailed",
        "Provisioning the delivery driver account failed.");

    public async Task<Result<Guid>> ProvisionDriverAsync(
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken = default)
    {
        var response = await provisionClient.GetResponse<ProvisionUserResponse, Error>(
            new ProvisionUserRequest(email, firstName, lastName, DeliveryDriverRole),
            cancellationToken);

        if (response.Is(out Response<Error> errorResponse))
        {
            return Result.Failure<Guid>(errorResponse.Message);
        }

        if (response.Is(out Response<ProvisionUserResponse> provisionResponse))
        {
            return provisionResponse.Message.UserId;
        }

        return Result.Failure<Guid>(ProvisioningFailed);
    }

    public async Task<Result> DeactivateDriverUserAsync(Guid userId, CancellationToken cancellationToken = default)
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
