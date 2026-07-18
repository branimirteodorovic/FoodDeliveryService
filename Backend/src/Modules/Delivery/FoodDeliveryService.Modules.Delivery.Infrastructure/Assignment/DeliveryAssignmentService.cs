using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Assignment;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Locations;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DeliveryAggregate = FoodDeliveryService.Modules.Delivery.Domain.Deliveries.Delivery;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Assignment;

/// <summary>
/// The offer routine (plan §6.2): find the nearest available driver this delivery has not tried
/// yet and offer it to them, or park the delivery as Unassigned when candidates are exhausted.
/// Saves the unit of work itself so a caller that staged prior state on the same tracked aggregate
/// (a rejection, an expiry) commits atomically with the new offer.
/// </summary>
internal sealed class DeliveryAssignmentService(
    IDeliveriesRepository deliveriesRepository,
    IDriverLocationStore driverLocationStore,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork,
    IOptions<DeliveryAssignmentOptions> options,
    ILogger<DeliveryAssignmentService> logger) : IDeliveryAssignmentService
{
    private readonly DeliveryAssignmentOptions _options = options.Value;

    public async Task<Result> OfferNextAsync(Guid deliveryId, CancellationToken cancellationToken = default)
    {
        DeliveryAggregate? delivery = await deliveriesRepository.GetAsync(deliveryId, cancellationToken);

        if (delivery is null)
        {
            return Result.Failure(DeliveryErrors.NotFound(deliveryId));
        }

        // Idempotent re-entry: a redelivered event or a raced job tick finds the delivery already
        // offered/assigned/terminal and must not push it anywhere.
        if (delivery.Status != DeliveryStatus.Pending)
        {
            return Result.Success();
        }

        IReadOnlyCollection<NearbyDriver> nearbyDrivers = await driverLocationStore.FindNearestAvailableAsync(
            delivery.PickupLocation,
            _options.SearchRadiusKm,
            _options.CandidateLimit,
            cancellationToken);

        Guid? candidateId = delivery.SelectNextCandidate(nearbyDrivers.Select(d => d.DriverId));

        Result result;

        if (candidateId is null)
        {
            logger.LogWarning(
                "Delivery {DeliveryId} (order {OrderId}): no untried available drivers within {RadiusKm} km " +
                "of the pickup — parking as Unassigned ({TriedCount} tried)",
                delivery.Id,
                delivery.OrderId,
                _options.SearchRadiusKm,
                delivery.TriedDriverIds.Count);

            result = delivery.MarkUnassigned();
        }
        else
        {
            DateTime offerExpiresOnUtc = dateTimeProvider.UtcNow.AddSeconds(_options.OfferWindowInSeconds);

            logger.LogInformation(
                "Delivery {DeliveryId} (order {OrderId}): offering to driver {DriverId} until {OfferExpiresOnUtc}",
                delivery.Id,
                delivery.OrderId,
                candidateId.Value,
                offerExpiresOnUtc);

            result = delivery.OfferTo(candidateId.Value, offerExpiresOnUtc);
        }

        if (result.IsFailure)
        {
            return result;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
