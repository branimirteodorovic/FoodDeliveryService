using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Locations;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;

namespace FoodDeliveryService.Modules.Delivery.Application.Drivers.SetDriverAvailability;

internal sealed class SetDriverAvailabilityCommandHandler(
    IDriversRepository driversRepository,
    IDeliveryContext deliveryContext,
    IDriverLocationStore driverLocationStore,
    IUnitOfWork unitOfWork)
    : ICommandHandler<SetDriverAvailabilityCommand>
{
    public async Task<Result> Handle(SetDriverAvailabilityCommand request, CancellationToken cancellationToken)
    {
        Driver? driver = await driversRepository.GetAsync(deliveryContext.UserId, cancellationToken);

        if (driver is null)
        {
            return Result.Failure(DriverErrors.NotOnboarded);
        }

        Result transitionResult = request.Available ? driver.GoAvailable() : driver.GoOffline();

        if (transitionResult.IsFailure)
        {
            return transitionResult;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The pool is a Redis-side projection of the status just committed, so it is updated after
        // the save: a failed transition must not touch it. The reverse order would leave an
        // offline driver taking offers if the save then rolled back.
        if (request.Available)
        {
            await driverLocationStore.EnterAvailablePoolAsync(driver.Id, cancellationToken);
        }
        else
        {
            await driverLocationStore.LeaveAvailablePoolAsync(driver.Id, cancellationToken);
        }

        return Result.Success();
    }
}
