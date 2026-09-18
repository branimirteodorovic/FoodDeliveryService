using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Orders.Domain.Orders;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.MarkPaymentCaptured;

internal sealed class MarkPaymentCapturedCommandHandler(
    IOrdersRepository ordersRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<MarkPaymentCapturedCommand>
{
    public async Task<Result> Handle(MarkPaymentCapturedCommand request, CancellationToken cancellationToken)
    {
        Order? order = await ordersRepository.GetAsync(request.OrderId, cancellationToken);

        if (order is null)
        {
            // A capture for an order this module does not have. Not absorbed: real money has moved,
            // and the only honest response is to say so on the inbox row where somebody reads it.
            return Result.Failure(OrderErrors.NotFound(request.OrderId));
        }

        Result result = order.MarkPaymentCaptured();

        if (result.IsFailure)
        {
            return result;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
