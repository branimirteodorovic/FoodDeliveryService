using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Assignment;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;
using DeliveryAggregate = FoodDeliveryService.Modules.Delivery.Domain.Deliveries.Delivery;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.ExpireDeliveryOffer;

internal sealed class ExpireDeliveryOfferCommandHandler(
    IDeliveriesRepository deliveriesRepository,
    IDeliveryAssignmentService deliveryAssignmentService,
    IDateTimeProvider dateTimeProvider)
    : ICommandHandler<ExpireDeliveryOfferCommand>
{
    public async Task<Result> Handle(ExpireDeliveryOfferCommand request, CancellationToken cancellationToken)
    {
        DeliveryAggregate? delivery = await deliveriesRepository.GetAsync(request.DeliveryId, cancellationToken);

        if (delivery is null)
        {
            return Result.Failure(DeliveryErrors.NotFound(request.DeliveryId));
        }

        DateTime utcNow = dateTimeProvider.UtcNow;

        // The job's SELECT and this load race the driver: an accept (or a reject + re-offer with a
        // fresh deadline) between the two makes the expiry moot — settle idempotently, don't fail.
        if (delivery.Status != DeliveryStatus.Offered || delivery.OfferExpiresOnUtc > utcNow)
        {
            return Result.Success();
        }

        Result expireResult = delivery.ExpireOffer(utcNow);

        if (expireResult.IsFailure)
        {
            return expireResult;
        }

        // Same single-transaction pattern as the reject handler: the offer routine's save persists
        // the expiry together with the next offer (or Unassigned).
        return await deliveryAssignmentService.OfferNextAsync(delivery.Id, cancellationToken);
    }
}
