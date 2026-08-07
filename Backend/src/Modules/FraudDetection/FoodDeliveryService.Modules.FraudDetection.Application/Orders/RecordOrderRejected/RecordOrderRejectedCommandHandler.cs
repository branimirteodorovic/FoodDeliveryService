using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.FraudDetection.Application.Abstractions.Data;
using FoodDeliveryService.Modules.FraudDetection.Application.Behaviours;
using FoodDeliveryService.Modules.FraudDetection.Domain.Customers;
using FoodDeliveryService.Modules.FraudDetection.Domain.Orders;

namespace FoodDeliveryService.Modules.FraudDetection.Application.Orders.RecordOrderRejected;

internal sealed class RecordOrderRejectedCommandHandler(
    IOrderFactsRepository orderFacts,
    ICustomerBehavioursRepository customerBehaviours,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RecordOrderRejectedCommand>
{
    public async Task<Result> Handle(RecordOrderRejectedCommand request, CancellationToken cancellationToken)
    {
        OrderFact? fact = await orderFacts.GetAsync(request.OrderId, cancellationToken);

        if (fact is not null && !fact.IsOpen)
        {
            return Result.Success();
        }

        fact?.MarkRejected(request.RejectedOnUtc);

        CustomerBehaviour behaviour = await customerBehaviours.GetOrCreateAsync(
            request.CustomerId,
            request.RejectedOnUtc,
            cancellationToken);

        behaviour.RecordOrderRejected();

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
