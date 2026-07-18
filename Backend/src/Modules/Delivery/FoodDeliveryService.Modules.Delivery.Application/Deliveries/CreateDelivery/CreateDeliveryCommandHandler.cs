using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Assignment;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;
using FoodDeliveryService.Modules.Delivery.Domain.Shared;
using DeliveryAggregate = FoodDeliveryService.Modules.Delivery.Domain.Deliveries.Delivery;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.CreateDelivery;

internal sealed class CreateDeliveryCommandHandler(
    IDeliveriesRepository deliveriesRepository,
    IDeliveryAssignmentService deliveryAssignmentService,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork)
    : ICommandHandler<CreateDeliveryCommand>
{
    public async Task<Result> Handle(CreateDeliveryCommand request, CancellationToken cancellationToken)
    {
        DeliveryAggregate? existing = await deliveriesRepository.GetByOrderIdAsync(request.OrderId, cancellationToken);

        Guid deliveryId;

        if (existing is null)
        {
            Result<GeoCoordinate> pickupLocation = GeoCoordinate.Create(
                request.PickupLatitude,
                request.PickupLongitude);

            if (pickupLocation.IsFailure)
            {
                return Result.Failure(pickupLocation.Error);
            }

            var dropoffAddress = new DeliveryAddress(
                request.DropoffStreet,
                request.DropoffCity,
                request.DropoffPostalCode,
                request.DropoffCountry,
                request.DropoffNotes,
                request.DropoffLatitude,
                request.DropoffLongitude);

            var delivery = DeliveryAggregate.Create(
                request.OrderId,
                request.RestaurantId,
                request.CustomerId,
                pickupLocation.Value,
                dropoffAddress,
                dateTimeProvider.UtcNow);

            deliveriesRepository.Insert(delivery);

            // Saved before the offer routine so the delivery record is durable even if the routine
            // fails — the inbox retry then finds it and only re-runs the offer. The unique index on
            // order_id catches a concurrent duplicate; that retry lands in the existing-branch.
            await unitOfWork.SaveChangesAsync(cancellationToken);

            deliveryId = delivery.Id;
        }
        else
        {
            deliveryId = existing.Id;
        }

        return await deliveryAssignmentService.OfferNextAsync(deliveryId, cancellationToken);
    }
}
