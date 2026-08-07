using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.FraudDetection.Application.Abstractions.Data;
using FoodDeliveryService.Modules.FraudDetection.Application.Behaviours;
using FoodDeliveryService.Modules.FraudDetection.Domain.Customers;
using FoodDeliveryService.Modules.FraudDetection.Domain.Orders;
using FoodDeliveryService.Modules.FraudDetection.Domain.Shared;

namespace FoodDeliveryService.Modules.FraudDetection.Application.Orders.RecordOrderReadyForPickup;

internal sealed class RecordOrderReadyForPickupCommandHandler(
    IOrderFactsRepository orderFacts,
    ICustomerBehavioursRepository customerBehaviours,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RecordOrderReadyForPickupCommand>
{
    public async Task<Result> Handle(
        RecordOrderReadyForPickupCommand request,
        CancellationToken cancellationToken)
    {
        OrderFact? fact = await orderFacts.GetAsync(request.OrderId, cancellationToken);

        if (fact is null)
        {
            // The event carries the whole order, so a missing placed event is recoverable here:
            // build the fact and count the order, which keeps the customer's placed counter honest
            // rather than silently short by one.
            fact = OrderFact.Create(
                request.OrderId,
                request.CustomerId,
                request.RestaurantId,
                request.Subtotal,
                request.PlacedOnUtc);

            orderFacts.Insert(fact);

            CustomerBehaviour behaviour = await customerBehaviours.GetOrCreateAsync(
                request.CustomerId,
                request.PlacedOnUtc,
                cancellationToken);

            behaviour.RecordOrderPlaced(request.Subtotal, request.PlacedOnUtc, BehaviourWindow.Length);
        }

        fact.MarkReadyForPickup(request.ReadyOnUtc, request.DropoffLatitude, request.DropoffLongitude);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
