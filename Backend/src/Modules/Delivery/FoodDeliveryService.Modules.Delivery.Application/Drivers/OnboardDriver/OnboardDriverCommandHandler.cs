using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Provisioning;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;
using Microsoft.Extensions.Logging;

namespace FoodDeliveryService.Modules.Delivery.Application.Drivers.OnboardDriver;

internal sealed class OnboardDriverCommandHandler(
    IDriverProvisioningService driverProvisioningService,
    IDriversRepository driversRepository,
    IUnitOfWork unitOfWork,
    ILogger<OnboardDriverCommandHandler> logger)
    : ICommandHandler<OnboardDriverCommand, Guid>
{
    public async Task<Result<Guid>> Handle(OnboardDriverCommand request, CancellationToken cancellationToken)
    {
        // Step 1 — provision the invited DeliveryDriver account in Users (RPC over the bus).
        // Users creates the Identity account (no password), assigns the role and triggers the
        // invitation email via UserInvitedIntegrationEvent; a duplicate email surfaces here as a
        // clean failure.
        Result<Guid> provisionResult = await driverProvisioningService.ProvisionDriverAsync(
            request.Email,
            request.FirstName,
            request.LastName,
            cancellationToken);

        if (provisionResult.IsFailure)
        {
            return Result.Failure<Guid>(provisionResult.Error);
        }

        // Step 2 — persist the Driver keyed by the returned UserId, in this module's own unit of
        // work. If this step fails the provisioned account is compensated away (below).
        Driver? existingDriver = await driversRepository.GetAsync(provisionResult.Value, cancellationToken);

        if (existingDriver is not null)
        {
            await CompensateProvisionedDriverAsync(provisionResult.Value, cancellationToken);

            return Result.Failure<Guid>(DriverErrors.AlreadyOnboarded);
        }

        var vehicleType = Enum.Parse<VehicleType>(request.VehicleType, ignoreCase: true);

        Result<Driver> driverResult = Driver.Onboard(
            provisionResult.Value,
            request.Email,
            request.FirstName,
            request.LastName,
            vehicleType,
            DateTime.UtcNow);

        if (driverResult.IsFailure)
        {
            await CompensateProvisionedDriverAsync(provisionResult.Value, cancellationToken);

            return Result.Failure<Guid>(driverResult.Error);
        }

        driversRepository.Insert(driverResult.Value);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            await CompensateProvisionedDriverAsync(provisionResult.Value, cancellationToken);

            throw;
        }

        return driverResult.Value.Id;
    }

    // Partial-failure compensation. A saga is overkill for this low-frequency, admin-driven
    // two-step flow. Best-effort: if the compensating call itself fails we only log; the orphaned
    // account is inert because it was never activated, and can be cleaned up manually.
    private async Task CompensateProvisionedDriverAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            Result result = await driverProvisioningService.DeactivateDriverUserAsync(userId, cancellationToken);

            if (result.IsFailure)
            {
                logger.LogError(
                    "Onboarding failed after driver provisioning; compensating deactivation of user {UserId} failed: {Error}",
                    userId,
                    result.Error);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Onboarding failed after driver provisioning; compensating deactivation of user {UserId} threw",
                userId);
        }
    }
}
