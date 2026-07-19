using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Orders.Domain.Orders;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.MarkOrderOutForDelivery;

internal sealed class MarkOrderOutForDeliveryCommandHandler(
    IOrdersRepository ordersRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<MarkOrderOutForDeliveryCommand>
{
    public async Task<Result> Handle(MarkOrderOutForDeliveryCommand request, CancellationToken cancellationToken)
    {
        Order? order = await ordersRepository.GetAsync(request.OrderId, cancellationToken);

        if (order is null)
        {
            return Result.Failure(OrderErrors.NotFound(request.OrderId));
        }

        Result result = order.MarkOutForDelivery();

        if (result.IsFailure)
        {
            return result;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
