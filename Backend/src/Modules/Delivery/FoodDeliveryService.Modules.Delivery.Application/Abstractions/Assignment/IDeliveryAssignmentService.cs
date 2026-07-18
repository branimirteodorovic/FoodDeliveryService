using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Delivery.Application.Abstractions.Assignment;

/// <summary>
/// The single place the "find the next driver and offer, or give up" logic lives. Invoked from
/// three callers — the OrderReadyForPickup consumer right after creating the delivery, the reject
/// command handler after a driver declines, and ProcessExpiredOffersJob after expiring a lapsed
/// offer — so it is idempotent (a delivery that is not Pending is left untouched) and
/// self-contained (it saves the unit of work itself, persisting any state the caller staged on the
/// same tracked aggregate in the same transaction).
/// </summary>
public interface IDeliveryAssignmentService
{
    Task<Result> OfferNextAsync(Guid deliveryId, CancellationToken cancellationToken = default);
}
