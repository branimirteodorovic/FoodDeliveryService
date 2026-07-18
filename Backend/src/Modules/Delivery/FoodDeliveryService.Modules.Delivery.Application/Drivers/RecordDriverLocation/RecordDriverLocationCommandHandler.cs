using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Locations;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;
using FoodDeliveryService.Modules.Delivery.Domain.Shared;

namespace FoodDeliveryService.Modules.Delivery.Application.Drivers.RecordDriverLocation;

/// <summary>
/// The system's highest-traffic path — one call per active driver every few seconds. By design it
/// does NOT go through the Driver aggregate or the outbox: a position is telemetry, not domain
/// state, and nothing else reacts to it. It validates the coordinate, refuses an offline driver,
/// and writes straight to the location store (Redis live position + Postgres history).
/// </summary>
internal sealed class RecordDriverLocationCommandHandler(
    IDriversRepository driversRepository,
    IDeliveryContext deliveryContext,
    IDriverLocationStore driverLocationStore)
    : ICommandHandler<RecordDriverLocationCommand>
{
    public async Task<Result> Handle(RecordDriverLocationCommand request, CancellationToken cancellationToken)
    {
        Driver? driver = await driversRepository.GetAsync(deliveryContext.UserId, cancellationToken);

        if (driver is null)
        {
            return Result.Failure(DriverErrors.NotOnboarded);
        }

        if (driver.Status == DriverStatus.Offline)
        {
            return Result.Failure(DriverErrors.Offline);
        }

        Result<GeoCoordinate> locationResult = GeoCoordinate.Create(request.Latitude, request.Longitude);

        if (locationResult.IsFailure)
        {
            return Result.Failure(locationResult.Error);
        }

        await driverLocationStore.RecordAsync(driver.Id, locationResult.Value, DateTime.UtcNow, cancellationToken);

        // Only an available driver belongs in the candidate pool. A Busy driver reports position
        // for the customer's tracking screen but must not be offered another delivery — they were
        // taken out of the pool on Reserve and stay out. Enrolling here (rather than in RecordAsync)
        // keeps the store's write path free of status logic and re-adds the driver at the position
        // just recorded, which is also how a freshly-available driver first enters the pool.
        if (driver.Status == DriverStatus.Available)
        {
            await driverLocationStore.EnterAvailablePoolAsync(driver.Id, cancellationToken);
        }

        return Result.Success();
    }
}
