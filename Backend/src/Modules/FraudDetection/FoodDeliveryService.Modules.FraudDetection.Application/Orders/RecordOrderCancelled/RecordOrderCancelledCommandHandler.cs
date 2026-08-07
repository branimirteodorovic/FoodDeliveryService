using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.FraudDetection.Application.Abstractions.Data;
using FoodDeliveryService.Modules.FraudDetection.Application.Behaviours;
using FoodDeliveryService.Modules.FraudDetection.Domain.Customers;
using FoodDeliveryService.Modules.FraudDetection.Domain.Orders;
using FoodDeliveryService.Modules.FraudDetection.Domain.Shared;

namespace FoodDeliveryService.Modules.FraudDetection.Application.Orders.RecordOrderCancelled;

internal sealed class RecordOrderCancelledCommandHandler(
    IOrderFactsRepository orderFacts,
    ICustomerBehavioursRepository customerBehaviours,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RecordOrderCancelledCommand>
{
    public async Task<Result> Handle(RecordOrderCancelledCommand request, CancellationToken cancellationToken)
    {
        OrderFact? fact = await orderFacts.GetAsync(request.OrderId, cancellationToken);

        // Already counted — a redelivery, or a second cancellation of an order that is already
        // closed. The counter must not move twice.
        if (fact is not null && !fact.IsOpen)
        {
            return Result.Success();
        }

        fact?.MarkCancelled(request.CancelledOnUtc);

        CustomerBehaviour behaviour = await customerBehaviours.GetOrCreateAsync(
            request.CustomerId,
            request.CancelledOnUtc,
            cancellationToken);

        // No fact row means the "before pickup" shape is unknowable, so it is not claimed. The
        // cancellation itself is still counted: the rate signal is the whole point of this handler
        // and the event carries everything it needs.
        behaviour.RecordOrderCancelled(
            request.CancelledOnUtc,
            fact?.CancelledBeforePickup ?? false,
            BehaviourWindow.Length);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
