using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.FraudDetection.Application.Abstractions.Data;
using FoodDeliveryService.Modules.FraudDetection.Domain.Orders;

namespace FoodDeliveryService.Modules.FraudDetection.Application.Deliveries.RecordDeliveryUnassigned;

internal sealed class RecordDeliveryUnassignedCommandHandler(
    IOrderFactsRepository orderFacts,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RecordDeliveryUnassignedCommand>
{
    public async Task<Result> Handle(
        RecordDeliveryUnassignedCommand request,
        CancellationToken cancellationToken)
    {
        OrderFact? fact = await orderFacts.GetAsync(request.OrderId, cancellationToken);

        if (fact is null)
        {
            return Result.Success();
        }

        fact.RecordUnassigned(request.UnassignedOnUtc);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
