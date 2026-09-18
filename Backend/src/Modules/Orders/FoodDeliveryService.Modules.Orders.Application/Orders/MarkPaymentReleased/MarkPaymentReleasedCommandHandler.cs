using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Orders.Domain.Orders;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.MarkPaymentReleased;

internal sealed class MarkPaymentReleasedCommandHandler(
    IOrdersRepository ordersRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<MarkPaymentReleasedCommand>
{
    public async Task<Result> Handle(MarkPaymentReleasedCommand request, CancellationToken cancellationToken)
    {
        Order? order = await ordersRepository.GetAsync(request.OrderId, cancellationToken);

        if (order is null)
        {
            // A release for an order this module does not have — the mirror of the capture handler, and
            // reported for the same reason: an order Payments knows about and Orders does not is a fault.
            return Result.Failure(OrderErrors.NotFound(request.OrderId));
        }

        Result result = order.MarkPaymentReleased();

        if (result.IsFailure)
        {
            return result;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
