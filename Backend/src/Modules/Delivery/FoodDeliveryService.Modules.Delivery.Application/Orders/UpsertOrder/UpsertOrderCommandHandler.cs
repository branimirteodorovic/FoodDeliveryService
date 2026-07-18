using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Delivery.Domain.Orders;
using FoodDeliveryService.Modules.Delivery.Domain.Shared;

namespace FoodDeliveryService.Modules.Delivery.Application.Orders.UpsertOrder;

internal sealed class UpsertOrderCommandHandler(
    IOrdersRepository ordersRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpsertOrderCommand>
{
    public async Task<Result> Handle(UpsertOrderCommand request, CancellationToken cancellationToken)
    {
        var deliveryAddress = new DeliveryAddress(
            request.DeliveryStreet,
            request.DeliveryCity,
            request.DeliveryPostalCode,
            request.DeliveryCountry,
            request.DeliveryNotes,
            request.DeliveryLatitude,
            request.DeliveryLongitude);

        Order? order = await ordersRepository.GetAsync(request.OrderId, cancellationToken);

        if (order is null)
        {
            ordersRepository.Insert(
                Order.Create(
                    request.OrderId,
                    request.CustomerId,
                    request.RestaurantId,
                    deliveryAddress,
                    request.PlacedOnUtc));
        }
        else
        {
            order.Update(request.CustomerId, request.RestaurantId, deliveryAddress, request.PlacedOnUtc);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
