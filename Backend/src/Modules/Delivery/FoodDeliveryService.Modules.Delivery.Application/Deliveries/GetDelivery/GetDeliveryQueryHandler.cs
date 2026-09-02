using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Locations;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetDelivery;

internal sealed class GetDeliveryQueryHandler(
    IDbConnectionFactory dbConnectionFactory,
    IDriverLocationStore driverLocationStore,
    IDeliveryContext deliveryContext)
    : IQueryHandler<GetDeliveryQuery, DeliveryResponse>
{
    public async Task<Result<DeliveryResponse>> Handle(GetDeliveryQuery request, CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        // The ownership check is part of the WHERE clause, not a branch after the read: a caller who
        // is neither the customer, the assigned driver nor an administrator gets no row, and so the
        // 404 below. Returning a distinguishable "not yours" here would confirm that the id exists,
        // which is exactly what somebody guessing delivery ids is trying to learn.
        const string sql =
            $"""
             {DeliveryDetailRow.SelectSql}
             WHERE d.id = @DeliveryId AND {DeliveryAccess.VisibleToCallerSql}
             """;

        DeliveryDetailRow? row = await connection.QuerySingleOrDefaultAsync<DeliveryDetailRow>(
            sql,
            new
            {
                request.DeliveryId,
                deliveryContext.UserId,
                IsAdmin = DeliveryAccess.CanViewAnyDelivery(deliveryContext)
            });

        if (row is null)
        {
            return Result.Failure<DeliveryResponse>(DeliveryErrors.NotFound(request.DeliveryId));
        }

        // The live position is only meaningful while the driver is on this delivery; once it is
        // terminal the driver may already be reserved for another order.
        DriverLocation? currentLocation = null;

        if (row.DriverId is not null && row.Status is DeliveryStatus.Assigned or DeliveryStatus.PickedUp)
        {
            currentLocation = await driverLocationStore.GetCurrentAsync(row.DriverId.Value, cancellationToken);
        }

        return row.ToResponse(currentLocation);
    }
}
