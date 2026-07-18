using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Provisioning;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;
using Microsoft.Extensions.Logging;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.OnboardRestaurant;

internal sealed class OnboardRestaurantCommandHandler(
    IManagerProvisioningService managerProvisioningService,
    IRestaurantsRepository restaurantsRepository,
    IUnitOfWork unitOfWork,
    ILogger<OnboardRestaurantCommandHandler> logger)
    : ICommandHandler<OnboardRestaurantCommand, Guid>
{
    public async Task<Result<Guid>> Handle(OnboardRestaurantCommand request, CancellationToken cancellationToken)
    {
        // Step 1 — provision the invited RestaurantManager account in Users (RPC over the bus).
        // Users creates the Identity account (no password), assigns the role and triggers the
        // invitation email via UserInvitedIntegrationEvent; a duplicate email surfaces here as a
        // clean failure.
        Result<Guid> provisionResult = await managerProvisioningService.ProvisionManagerAsync(
            request.ManagerEmail,
            request.ManagerFirstName,
            request.ManagerLastName,
            cancellationToken);

        if (provisionResult.IsFailure)
        {
            return Result.Failure<Guid>(provisionResult.Error);
        }

        // Step 2 — persist the Restaurant with the returned manager id, in this module's own
        // unit of work. If this step fails the manager account is compensated away (below).
        Result<Address> addressResult = Address.Create(
            request.Street,
            request.City,
            request.PostalCode,
            request.Country,
            request.Latitude,
            request.Longitude);

        if (addressResult.IsFailure)
        {
            await CompensateProvisionedManagerAsync(provisionResult.Value, cancellationToken);

            return Result.Failure<Guid>(addressResult.Error);
        }

        Result<Restaurant> restaurantResult = Restaurant.Create(
            provisionResult.Value,
            request.Name,
            request.TaxIdentification,
            request.CuisineType,
            request.Email,
            request.PhoneNumber,
            addressResult.Value,
            request.CommissionRate,
            DateTime.UtcNow);

        if (restaurantResult.IsFailure)
        {
            await CompensateProvisionedManagerAsync(provisionResult.Value, cancellationToken);

            return Result.Failure<Guid>(restaurantResult.Error);
        }

        restaurantsRepository.Insert(restaurantResult.Value);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            await CompensateProvisionedManagerAsync(provisionResult.Value, cancellationToken);

            throw;
        }

        return restaurantResult.Value.Id;
    }

    // Partial-failure compensation. A saga is
    // overkill for this low-frequency, admin-driven two-step flow. Best-effort: if the compensating
    // call itself fails we only log; the orphaned account is inert because it was never activated,
    // and can be cleaned up manually.
    private async Task CompensateProvisionedManagerAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            Result result = await managerProvisioningService.DeactivateManagerAsync(userId, cancellationToken);

            if (result.IsFailure)
            {
                logger.LogError(
                    "Onboarding failed after manager provisioning; compensating deactivation of user {UserId} failed: {Error}",
                    userId,
                    result.Error);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Onboarding failed after manager provisioning; compensating deactivation of user {UserId} threw",
                userId);
        }
    }
}
