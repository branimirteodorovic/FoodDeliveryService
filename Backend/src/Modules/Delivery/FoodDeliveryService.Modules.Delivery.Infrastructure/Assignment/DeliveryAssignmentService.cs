using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Locking;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Assignment;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Locations;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DeliveryAggregate = FoodDeliveryService.Modules.Delivery.Domain.Deliveries.Delivery;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Assignment;

/// <summary>
/// The offer routine (plan §6.2): find the nearest available driver this delivery has not tried
/// yet and offer it to them, or park the delivery as Unassigned when candidates are exhausted.
/// Saves the unit of work itself so a caller that staged prior state on the same tracked aggregate
/// (a rejection, an expiry) commits atomically with the new offer.
/// <para>
/// Two distributed locks make the routine safe against itself (Caching plan §5). The
/// <c>delivery:offer-lock:{deliveryId}</c> serializes the triggers that overlap in practice — a
/// rejection re-offer, a ProcessExpiredOffersJob tick (concurrent across replicas, since
/// DisallowConcurrentExecution is per-instance), a fresh create — which would otherwise both read
/// the delivery as Pending and both offer it. The <c>delivery:driver-lock:{driverId}</c> is taken
/// once a candidate is chosen, so two *different* deliveries cannot select and offer the same
/// nearest driver in the same instant; it is held only for this routine's transaction, which is
/// what keeps a driver legitimately offerable for several deliveries in sequence (an offer is not
/// a reservation — accepting one is, see AcceptDeliveryOfferCommandHandler).
/// </para>
/// <para>
/// Known residue: callers that stage state on the aggregate before calling in (reject, expire)
/// load it *outside* this lock, so a caller that loses the race still holds the snapshot it read
/// before and EF's identity map returns that tracked instance here. The lock removes the
/// double-offer; closing the stale-snapshot window as well needs an optimistic concurrency token
/// on the delivery row, which is deliberately out of this milestone's scope.
/// </para>
/// </summary>
internal sealed class DeliveryAssignmentService(
    IDeliveriesRepository deliveriesRepository,
    IDriversRepository driversRepository,
    IDriverLocationStore driverLocationStore,
    IDistributedLock distributedLock,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork,
    IOptions<DeliveryAssignmentOptions> options,
    ILogger<DeliveryAssignmentService> logger) : IDeliveryAssignmentService
{
    private readonly DeliveryAssignmentOptions _options = options.Value;

    public async Task<Result> OfferNextAsync(Guid deliveryId, CancellationToken cancellationToken = default)
    {
        // Taken before the delivery is loaded, not after: the check-then-act this protects starts
        // at the read, so a lock acquired after it would still let both callers act on the same
        // Pending snapshot.
        await using IAsyncDisposable? offerLock = await distributedLock.TryAcquireAsync(
            DeliveryLocks.Offer(deliveryId),
            DeliveryLocks.Ttl,
            cancellationToken);

        if (offerLock is null)
        {
            logger.LogInformation(
                "Delivery {DeliveryId}: another trigger is running the offer routine — standing down",
                deliveryId);

            return Result.Failure(DeliveryErrors.AssignmentInProgress);
        }

        return await OfferNextCoreAsync(deliveryId, cancellationToken);
    }

    private async Task<Result> OfferNextCoreAsync(Guid deliveryId, CancellationToken cancellationToken)
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

        var candidateIds = nearbyDrivers.Select(d => d.DriverId).ToList();

        // Set when a candidate is passed over for a reason that says nothing about this delivery —
        // someone else holds that driver's lock, or the geo pool was stale and they are no longer
        // available. Such a delivery must NOT be parked as Unassigned (that is the "nobody could
        // ever take this" outcome, and it waits on a human): it is simply retryable.
        var skippedCandidate = false;

        while (true)
        {
            Guid? candidateId = delivery.SelectNextCandidate(candidateIds);

            if (candidateId is null)
            {
                break;
            }

            IAsyncDisposable? driverLock = await distributedLock.TryAcquireAsync(
                DeliveryLocks.Driver(candidateId.Value),
                DeliveryLocks.Ttl,
                cancellationToken);

            if (driverLock is null)
            {
                logger.LogInformation(
                    "Delivery {DeliveryId}: driver {DriverId} is being assigned elsewhere — trying the next candidate",
                    delivery.Id,
                    candidateId.Value);

                skippedCandidate = true;
                candidateIds.Remove(candidateId.Value);
                continue;
            }

            await using (driverLock)
            {
                // Re-verify inside the lock. The geo search is a snapshot taken before the lock was
                // held, and the accept path removes a driver from the pool only after it commits —
                // so the aggregate, not Redis, is the authority on whether they are still free.
                Driver? driver = await driversRepository.GetAsync(candidateId.Value, cancellationToken);

                if (driver is null || driver.Status != DriverStatus.Available)
                {
                    logger.LogInformation(
                        "Delivery {DeliveryId}: driver {DriverId} is no longer available — trying the next candidate",
                        delivery.Id,
                        candidateId.Value);

                    skippedCandidate = true;
                    candidateIds.Remove(candidateId.Value);
                    continue;
                }

                DateTime offerExpiresOnUtc = dateTimeProvider.UtcNow.AddSeconds(_options.OfferWindowInSeconds);

                logger.LogInformation(
                    "Delivery {DeliveryId} (order {OrderId}): offering to driver {DriverId} until {OfferExpiresOnUtc}",
                    delivery.Id,
                    delivery.OrderId,
                    candidateId.Value,
                    offerExpiresOnUtc);

                Result offerResult = delivery.OfferTo(candidateId.Value, offerExpiresOnUtc);

                if (offerResult.IsFailure)
                {
                    return offerResult;
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Success();
            }
        }

        if (skippedCandidate)
        {
            logger.LogInformation(
                "Delivery {DeliveryId} (order {OrderId}): every candidate was locked or no longer available — " +
                "leaving it Pending for the next trigger",
                delivery.Id,
                delivery.OrderId);

            return Result.Failure(DeliveryErrors.AssignmentInProgress);
        }

        logger.LogWarning(
            "Delivery {DeliveryId} (order {OrderId}): no untried available drivers within {RadiusKm} km " +
            "of the pickup — parking as Unassigned ({TriedCount} tried)",
            delivery.Id,
            delivery.OrderId,
            _options.SearchRadiusKm,
            delivery.TriedDriverIds.Count);

        Result unassignedResult = delivery.MarkUnassigned();

        if (unassignedResult.IsFailure)
        {
            return unassignedResult;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
