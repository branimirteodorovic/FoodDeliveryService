using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;
using DeliveryAggregate = FoodDeliveryService.Modules.Delivery.Domain.Deliveries.Delivery;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.MarkDeliveryPickedUp;

// Ownership is enforced in the domain — MarkPickedUp only succeeds for the assigned driver, which
// is the authenticated caller here. The driver stays Busy (they are still on the delivery), so the
// pool is untouched.
internal sealed class MarkDeliveryPickedUpCommandHandler(
    IDeliveriesRepository deliveriesRepository,
    IDeliveryContext deliveryContext,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork)
    : ICommandHandler<MarkDeliveryPickedUpCommand>
{
    public async Task<Result> Handle(MarkDeliveryPickedUpCommand request, CancellationToken cancellationToken)
    {
        DeliveryAggregate? delivery = await deliveriesRepository.GetAsync(request.DeliveryId, cancellationToken);

        if (delivery is null)
        {
            return Result.Failure(DeliveryErrors.NotFound(request.DeliveryId));
        }

        Result pickedUpResult = delivery.MarkPickedUp(deliveryContext.UserId, dateTimeProvider.UtcNow);

        if (pickedUpResult.IsFailure)
        {
            return pickedUpResult;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
