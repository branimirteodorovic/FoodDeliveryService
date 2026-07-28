using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Locking;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Assignment;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Locations;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;
using DeliveryAggregate = FoodDeliveryService.Modules.Delivery.Domain.Deliveries.Delivery;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.AcceptDeliveryOffer;

internal sealed class AcceptDeliveryOfferCommandHandler(
    IDeliveriesRepository deliveriesRepository,
    IDriversRepository driversRepository,
    IDeliveryContext deliveryContext,
    IDriverLocationStore driverLocationStore,
    IDistributedLock distributedLock,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork)
    : ICommandHandler<AcceptDeliveryOfferCommand>
{
    public async Task<Result> Handle(AcceptDeliveryOfferCommand request, CancellationToken cancellationToken)
    {
        Guid driverId = deliveryContext.UserId;

        // Taken before anything is read (Caching plan §5). The Available → Busy guard below is a
        // check-then-act across two transactions: a driver holding offers for two deliveries who
        // accepts both at once has both requests read them as Available and both reserve them —
        // the delivery row carries no concurrency token to catch the second write. Serializing on
        // the driver makes the loser observe the committed Busy and fail cleanly.
        await using IAsyncDisposable? driverLock = await distributedLock.TryAcquireAsync(
            DeliveryLocks.Driver(driverId),
            DeliveryLocks.Ttl,
            cancellationToken);

        if (driverLock is null)
        {
            return Result.Failure(DeliveryErrors.AssignmentInProgress);
        }

        DeliveryAggregate? delivery = await deliveriesRepository.GetAsync(request.DeliveryId, cancellationToken);

        if (delivery is null)
        {
            return Result.Failure(DeliveryErrors.NotFound(request.DeliveryId));
        }

        Result acceptResult = delivery.AcceptOffer(driverId, dateTimeProvider.UtcNow);

        if (acceptResult.IsFailure)
        {
            return acceptResult;
        }

        Driver? driver = await driversRepository.GetAsync(driverId, cancellationToken);

        if (driver is null)
        {
            return Result.Failure(DriverErrors.NotOnboarded);
        }

        // Available → Busy inside the accepting transaction is what stops two deliveries grabbing
        // the same driver: the second accept finds them already Busy and fails cleanly, rolling the
        // delivery's Assigned state back with it. This aggregate-level guard stays the rule of
        // record — the lock above only makes sure two concurrent accepts observe it in sequence.
        Result reserveResult = driver.Reserve();

        if (reserveResult.IsFailure)
        {
            return reserveResult;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Pool membership is a Redis-side projection of the status just committed, so it is
        // updated only after the save succeeds — a failed accept must not evict the driver.
        await driverLocationStore.LeaveAvailablePoolAsync(driverId, cancellationToken);

        return Result.Success();
    }
}
