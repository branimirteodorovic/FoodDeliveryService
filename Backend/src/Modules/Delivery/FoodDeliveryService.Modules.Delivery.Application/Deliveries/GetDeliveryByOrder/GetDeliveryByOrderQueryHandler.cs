using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Locations;
using FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetDelivery;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetDeliveryByOrder;

internal sealed class GetDeliveryByOrderQueryHandler(
    IDbConnectionFactory dbConnectionFactory,
    IDriverLocationStore driverLocationStore,
    IDeliveryContext deliveryContext)
    : IQueryHandler<GetDeliveryByOrderQuery, DeliveryResponse>
{
    public async Task<Result<DeliveryResponse>> Handle(GetDeliveryByOrderQuery request, CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        // Ownership lives in the WHERE clause here too — see GetDeliveryQueryHandler. Tracking by
        // order id is the path a customer actually uses, so it is the one most worth not turning
        // into an existence oracle over other people's orders.
        const string sql =
            $"""
             {DeliveryDetailRow.SelectSql}
             WHERE d.order_id = @OrderId AND {DeliveryAccess.VisibleToCallerSql}
             """;

        DeliveryDetailRow? row = await connection.QuerySingleOrDefaultAsync<DeliveryDetailRow>(
            sql,
            new
            {
                request.OrderId,
                deliveryContext.UserId,
                IsAdmin = DeliveryAccess.CanViewAnyDelivery(deliveryContext)
            });

        if (row is null)
        {
            return Result.Failure<DeliveryResponse>(DeliveryErrors.NotFoundForOrder(request.OrderId));
        }

        DriverLocation? currentLocation = null;

        if (row.DriverId is not null && row.Status is DeliveryStatus.Assigned or DeliveryStatus.PickedUp)
        {
            currentLocation = await driverLocationStore.GetCurrentAsync(row.DriverId.Value, cancellationToken);
        }

        return row.ToResponse(currentLocation);
    }
}
