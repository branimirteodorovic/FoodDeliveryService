using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Orders.Domain.Orders;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.MarkPaymentAuthorized;

internal sealed class MarkPaymentAuthorizedCommandHandler(
    IOrdersRepository ordersRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<MarkPaymentAuthorizedCommand>
{
    public async Task<Result> Handle(MarkPaymentAuthorizedCommand request, CancellationToken cancellationToken)
    {
        Order? order = await ordersRepository.GetAsync(request.OrderId, cancellationToken);

        if (order is null)
        {
            // An authorization for an order this module does not have. Not absorbed: the payment
            // exists, real money is held on a card, and the only honest response is to say so on
            // the inbox row where somebody will read it.
            return Result.Failure(OrderErrors.NotFound(request.OrderId));
        }

        Result result = order.MarkPaymentAuthorized();

        if (result.IsFailure)
        {
            return result;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
