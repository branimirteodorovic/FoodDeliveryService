using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Orders.Domain.Orders;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.CancelOrder;

// Customer-facing cancellation. Ownership here is the customer, not the restaurant manager — only
// the customer who placed the order may cancel it. The domain guards *when* a cancel is allowed
// (Pending/Accepted only).
internal sealed class CancelOrderCommandHandler(
    IOrdersRepository ordersRepository,
    IOrdersContext ordersContext,
    IUnitOfWork unitOfWork)
    : ICommandHandler<CancelOrderCommand>
{
    public async Task<Result> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        Order? order = await ordersRepository.GetAsync(request.OrderId, cancellationToken);

        if (order is null)
        {
            return Result.Failure(OrderErrors.NotFound(request.OrderId));
        }

        if (order.CustomerId != ordersContext.UserId)
        {
            return Result.Failure(OrderErrors.NotOwner);
        }

        Result cancelResult = order.Cancel(DateTime.UtcNow);

        if (cancelResult.IsFailure)
        {
            return cancelResult;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
