using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Orders.Domain.Orders;
using FoodDeliveryService.Modules.Orders.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.StartPreparingOrder;

internal sealed class StartPreparingOrderCommandHandler(
    IOrdersRepository ordersRepository,
    IRestaurantReplicaRepository restaurantReplicaRepository,
    IOrdersContext ordersContext,
    IUnitOfWork unitOfWork)
    : ICommandHandler<StartPreparingOrderCommand>
{
    public async Task<Result> Handle(StartPreparingOrderCommand request, CancellationToken cancellationToken)
    {
        Order? order = await ordersRepository.GetAsync(request.OrderId, cancellationToken);

        if (order is null)
        {
            return Result.Failure(OrderErrors.NotFound(request.OrderId));
        }

        Restaurant? restaurant = await restaurantReplicaRepository.GetAsync(order.RestaurantId, cancellationToken);

        if (restaurant is null)
        {
            return Result.Failure(OrderErrors.RestaurantNotFound(order.RestaurantId));
        }

        Result ownershipResult = OrderOwnership.EnsureCanManage(restaurant, order.Id, ordersContext);

        if (ownershipResult.IsFailure)
        {
            return ownershipResult;
        }

        Result preparingResult = order.StartPreparing();

        if (preparingResult.IsFailure)
        {
            return preparingResult;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
