using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Assignment;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;
using DeliveryAggregate = FoodDeliveryService.Modules.Delivery.Domain.Deliveries.Delivery;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.RejectDeliveryOffer;

internal sealed class RejectDeliveryOfferCommandHandler(
    IDeliveriesRepository deliveriesRepository,
    IDeliveryContext deliveryContext,
    IDeliveryAssignmentService deliveryAssignmentService)
    : ICommandHandler<RejectDeliveryOfferCommand>
{
    public async Task<Result> Handle(RejectDeliveryOfferCommand request, CancellationToken cancellationToken)
    {
        DeliveryAggregate? delivery = await deliveriesRepository.GetAsync(request.DeliveryId, cancellationToken);

        if (delivery is null)
        {
            return Result.Failure(DeliveryErrors.NotFound(request.DeliveryId));
        }

        Result rejectResult = delivery.RejectOffer(deliveryContext.UserId);

        if (rejectResult.IsFailure)
        {
            return rejectResult;
        }

        // No save here: the offer routine loads the same tracked aggregate (EF identity map), so
        // its save persists the rejection and the next offer (or Unassigned) in one transaction —
        // a failure rolls both back and the delivery is never left silently parked in Pending.
        return await deliveryAssignmentService.OfferNextAsync(delivery.Id, cancellationToken);
    }
}
