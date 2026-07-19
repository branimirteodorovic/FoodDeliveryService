using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Locations;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;
using DeliveryAggregate = FoodDeliveryService.Modules.Delivery.Domain.Deliveries.Delivery;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.MarkDeliveryDelivered;

// Ownership is enforced in the domain — MarkDelivered only succeeds for the assigned driver. On the
// last mile the driver returns to the pool: Busy → Available and re-enters the geo set, exactly as
// the cancellation path does (mirrors CancelDeliveryCommandHandler).
internal sealed class MarkDeliveryDeliveredCommandHandler(
    IDeliveriesRepository deliveriesRepository,
    IDriversRepository driversRepository,
    IDeliveryContext deliveryContext,
    IDriverLocationStore driverLocationStore,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork)
    : ICommandHandler<MarkDeliveryDeliveredCommand>
{
    public async Task<Result> Handle(MarkDeliveryDeliveredCommand request, CancellationToken cancellationToken)
    {
        DeliveryAggregate? delivery = await deliveriesRepository.GetAsync(request.DeliveryId, cancellationToken);

        if (delivery is null)
        {
            return Result.Failure(DeliveryErrors.NotFound(request.DeliveryId));
        }

        Guid driverId = deliveryContext.UserId;

        Result deliveredResult = delivery.MarkDelivered(driverId, dateTimeProvider.UtcNow);

        if (deliveredResult.IsFailure)
        {
            return deliveredResult;
        }

        Driver? driver = await driversRepository.GetAsync(driverId, cancellationToken);

        if (driver is null)
        {
            return Result.Failure(DriverErrors.NotOnboarded);
        }

        Result releaseResult = driver.Release();

        if (releaseResult.IsFailure)
        {
            return releaseResult;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // After the save, as everywhere: the pool reflects committed state only. The driver
        // re-enters at their last fresh position; if it has gone stale this is a no-op and their
        // next report enrolls them.
        await driverLocationStore.EnterAvailablePoolAsync(driverId, cancellationToken);

        return Result.Success();
    }
}
