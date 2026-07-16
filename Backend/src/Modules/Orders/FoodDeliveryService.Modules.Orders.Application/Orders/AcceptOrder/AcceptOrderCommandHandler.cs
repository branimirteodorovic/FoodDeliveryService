using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Orders.Domain.Orders;
using FoodDeliveryService.Modules.Orders.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.AcceptOrder;

internal sealed class AcceptOrderCommandHandler(
    IOrdersRepository ordersRepository,
    IRestaurantReplicaRepository restaurantReplicaRepository,
    IOrdersContext ordersContext,
    IUnitOfWork unitOfWork)
    : ICommandHandler<AcceptOrderCommand>
{
    public async Task<Result> Handle(AcceptOrderCommand request, CancellationToken cancellationToken)
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

        Result ownershipResult = OrderOwnership.EnsureCanManage(restaurant, ordersContext);

        if (ownershipResult.IsFailure)
        {
            return ownershipResult;
        }

        Result acceptResult = order.Accept(DateTime.UtcNow);

        if (acceptResult.IsFailure)
        {
            return acceptResult;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
