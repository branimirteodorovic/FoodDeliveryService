using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.FraudDetection.Application.Abstractions.Data;
using FoodDeliveryService.Modules.FraudDetection.Application.Behaviours;
using FoodDeliveryService.Modules.FraudDetection.Domain.Drivers;
using FoodDeliveryService.Modules.FraudDetection.Domain.Orders;

namespace FoodDeliveryService.Modules.FraudDetection.Application.Deliveries.RecordOrderPickedUp;

internal sealed class RecordOrderPickedUpCommandHandler(
    IOrderFactsRepository orderFacts,
    IDriverBehavioursRepository driverBehaviours,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RecordOrderPickedUpCommand>
{
    public async Task<Result> Handle(RecordOrderPickedUpCommand request, CancellationToken cancellationToken)
    {
        OrderFact? fact = await orderFacts.GetAsync(request.OrderId, cancellationToken);

        // Gate the counter on the fact still being open and not already past pickup, so a
        // redelivery cannot inflate the driver's throughput.
        if (fact is not null && (!fact.IsOpen || fact.PickedUpOnUtc is not null))
        {
            return Result.Success();
        }

        fact?.MarkPickedUp(request.DeliveryId, request.DriverId, request.PickedUpOnUtc);

        DriverBehaviour driver = await driverBehaviours.GetOrCreateAsync(
            request.DriverId,
            request.PickedUpOnUtc,
            cancellationToken);

        driver.RecordPickup();

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
