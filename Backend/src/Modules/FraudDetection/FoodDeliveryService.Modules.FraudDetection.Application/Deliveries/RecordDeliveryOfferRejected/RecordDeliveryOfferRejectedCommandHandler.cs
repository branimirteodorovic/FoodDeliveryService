using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.FraudDetection.Application.Abstractions.Data;
using FoodDeliveryService.Modules.FraudDetection.Application.Behaviours;
using FoodDeliveryService.Modules.FraudDetection.Domain.Drivers;

namespace FoodDeliveryService.Modules.FraudDetection.Application.Deliveries.RecordDeliveryOfferRejected;

internal sealed class RecordDeliveryOfferRejectedCommandHandler(
    IDriverBehavioursRepository driverBehaviours,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RecordDeliveryOfferRejectedCommand>
{
    public async Task<Result> Handle(
        RecordDeliveryOfferRejectedCommand request,
        CancellationToken cancellationToken)
    {
        DriverBehaviour driver = await driverBehaviours.GetOrCreateAsync(
            request.DriverId,
            request.RejectedOnUtc,
            cancellationToken);

        // No local state distinguishes a redelivery here — one driver can legitimately reject the
        // same delivery's re-offer more than once. The inbox's per-consumer de-duplication is what
        // keeps this counter honest.
        driver.RecordOfferRejected();

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
