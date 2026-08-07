using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.FraudDetection.Application.Abstractions.Data;
using FoodDeliveryService.Modules.FraudDetection.Application.Behaviours;
using FoodDeliveryService.Modules.FraudDetection.Domain.Customers;
using FoodDeliveryService.Modules.FraudDetection.Domain.Drivers;
using FoodDeliveryService.Modules.FraudDetection.Domain.Orders;

namespace FoodDeliveryService.Modules.FraudDetection.Application.Deliveries.RecordOrderDelivered;

internal sealed class RecordOrderDeliveredCommandHandler(
    IOrderFactsRepository orderFacts,
    ICustomerBehavioursRepository customerBehaviours,
    IDriverBehavioursRepository driverBehaviours,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RecordOrderDeliveredCommand>
{
    public async Task<Result> Handle(RecordOrderDeliveredCommand request, CancellationToken cancellationToken)
    {
        OrderFact? fact = await orderFacts.GetAsync(request.OrderId, cancellationToken);

        if (fact is not null && !fact.IsOpen)
        {
            return Result.Success();
        }

        fact?.MarkDelivered(request.DeliveryId, request.DriverId, request.DeliveredOnUtc);

        // The delivery event carries no customer — it is the fact row that knows whose order this
        // was. Without one, only the driver side can be counted.
        if (fact is not null)
        {
            CustomerBehaviour customer = await customerBehaviours.GetOrCreateAsync(
                fact.CustomerId,
                request.DeliveredOnUtc,
                cancellationToken);

            customer.RecordOrderDelivered();
        }

        DriverBehaviour driver = await driverBehaviours.GetOrCreateAsync(
            request.DriverId,
            request.DeliveredOnUtc,
            cancellationToken);

        driver.RecordDeliveryCompleted(request.DeliveredOnUtc);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
