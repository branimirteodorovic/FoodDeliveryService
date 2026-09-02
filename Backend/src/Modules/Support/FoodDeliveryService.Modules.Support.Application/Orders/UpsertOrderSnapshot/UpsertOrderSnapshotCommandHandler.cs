using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Support.Domain.Orders;

namespace FoodDeliveryService.Modules.Support.Application.Orders.UpsertOrderSnapshot;

// Upsert, not insert. The inbox dedupes on message id, but that only covers the same delivery of
// the same message: a replayed stream, or a row rebuilt after a restore, must converge on the same
// snapshot rather than failing on a primary-key collision.
internal sealed class UpsertOrderSnapshotCommandHandler(
    IOrderSnapshotRepository snapshotRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpsertOrderSnapshotCommand>
{
    public async Task<Result> Handle(UpsertOrderSnapshotCommand request, CancellationToken cancellationToken)
    {
        OrderSnapshot? snapshot = await snapshotRepository.GetAsync(request.OrderId, cancellationToken);

        if (snapshot is null)
        {
            snapshotRepository.Insert(
                OrderSnapshot.Create(
                    request.OrderId,
                    request.CustomerId,
                    request.RestaurantId,
                    request.Subtotal,
                    request.PlacedOnUtc,
                    request.OccurredOnUtc));
        }
        else
        {
            snapshot.ApplyPlaced(
                request.CustomerId,
                request.RestaurantId,
                request.Subtotal,
                request.PlacedOnUtc,
                request.OccurredOnUtc);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
