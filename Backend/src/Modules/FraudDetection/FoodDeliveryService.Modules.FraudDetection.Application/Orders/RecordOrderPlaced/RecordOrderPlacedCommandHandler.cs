using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.FraudDetection.Application.Abstractions.Data;
using FoodDeliveryService.Modules.FraudDetection.Application.Behaviours;
using FoodDeliveryService.Modules.FraudDetection.Domain.Customers;
using FoodDeliveryService.Modules.FraudDetection.Domain.Orders;
using FoodDeliveryService.Modules.FraudDetection.Domain.Shared;

namespace FoodDeliveryService.Modules.FraudDetection.Application.Orders.RecordOrderPlaced;

internal sealed class RecordOrderPlacedCommandHandler(
    IOrderFactsRepository orderFacts,
    ICustomerBehavioursRepository customerBehaviours,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RecordOrderPlacedCommand>
{
    public async Task<Result> Handle(RecordOrderPlacedCommand request, CancellationToken cancellationToken)
    {
        OrderFact? fact = await orderFacts.GetAsync(request.OrderId, cancellationToken);

        // The fact row is the idempotency key for the counter. The inbox already guarantees one
        // dispatch per consumer, but a counter that can only ever be double-counted by a redelivery
        // is worth making structurally safe: no insert, no increment.
        if (fact is not null)
        {
            return Result.Success();
        }

        orderFacts.Insert(
            OrderFact.Create(
                request.OrderId,
                request.CustomerId,
                request.RestaurantId,
                request.Subtotal,
                request.PlacedOnUtc));

        CustomerBehaviour behaviour = await customerBehaviours.GetOrCreateAsync(
            request.CustomerId,
            request.PlacedOnUtc,
            cancellationToken);

        behaviour.RecordOrderPlaced(request.Subtotal, request.PlacedOnUtc, BehaviourWindow.Length);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
