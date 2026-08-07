using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.FraudDetection.Application.Abstractions.Data;
using FoodDeliveryService.Modules.FraudDetection.Domain.Orders;

namespace FoodDeliveryService.Modules.FraudDetection.Application.Orders.RecordOrderAccepted;

internal sealed class RecordOrderAcceptedCommandHandler(
    IOrderFactsRepository orderFacts,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RecordOrderAcceptedCommand>
{
    public async Task<Result> Handle(RecordOrderAcceptedCommand request, CancellationToken cancellationToken)
    {
        OrderFact? fact = await orderFacts.GetAsync(request.OrderId, cancellationToken);

        // OrderPlaced has not been seen. Both events come from Orders and every inbox message is
        // dispatched in occurred-on order, so this is not the normal path — it means the placed
        // event was never delivered at all. The acceptance carries no subtotal, so there is no
        // honest fact row to build from it: the order is left out of the fact table rather than
        // recorded with a fabricated value. Success, not failure — the inbox does not retry, and
        // there is nothing here to retry towards.
        if (fact is null)
        {
            return Result.Success();
        }

        fact.MarkAccepted(request.AcceptedOnUtc);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
