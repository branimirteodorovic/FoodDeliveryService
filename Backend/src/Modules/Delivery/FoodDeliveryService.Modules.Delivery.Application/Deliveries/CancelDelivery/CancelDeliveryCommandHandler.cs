using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Locations;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;
using DeliveryAggregate = FoodDeliveryService.Modules.Delivery.Domain.Deliveries.Delivery;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.CancelDelivery;

internal sealed class CancelDeliveryCommandHandler(
    IDeliveriesRepository deliveriesRepository,
    IDriversRepository driversRepository,
    IDriverLocationStore driverLocationStore,
    IUnitOfWork unitOfWork)
    : ICommandHandler<CancelDeliveryCommand>
{
    public async Task<Result> Handle(CancelDeliveryCommand request, CancellationToken cancellationToken)
    {
        DeliveryAggregate? delivery = await deliveriesRepository.GetByOrderIdAsync(request.OrderId, cancellationToken);

        // An order cancelled before it ever went ready has no delivery leg — nothing to compensate.
        if (delivery is null)
        {
            return Result.Success();
        }

        // Only an accepted delivery reserved its driver; a merely-offered driver was never taken
        // out of the pool. Captured before Cancel() flips the status.
        bool releaseDriver = delivery.Status is DeliveryStatus.Assigned or DeliveryStatus.PickedUp;

        Result cancelResult = delivery.Cancel();

        if (cancelResult.IsFailure)
        {
            return cancelResult;
        }

        if (releaseDriver)
        {
            Driver? driver = await driversRepository.GetAsync(delivery.DriverId!.Value, cancellationToken);

            if (driver is not null)
            {
                Result releaseResult = driver.Release();

                if (releaseResult.IsFailure)
                {
                    return releaseResult;
                }
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // After the save, as everywhere: the pool reflects committed state only. The driver
        // re-enters at their last fresh position; if it has gone stale this is a no-op and their
        // next report enrolls them.
        if (releaseDriver)
        {
            await driverLocationStore.EnterAvailablePoolAsync(delivery.DriverId!.Value, cancellationToken);
        }

        return Result.Success();
    }
}
