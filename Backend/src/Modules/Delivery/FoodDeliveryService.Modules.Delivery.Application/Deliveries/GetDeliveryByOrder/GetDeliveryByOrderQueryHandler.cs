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

        const string sql =
            $"""
             {DeliveryDetailRow.SelectSql}
             WHERE d.order_id = @OrderId
             """;

        DeliveryDetailRow? row = await connection.QuerySingleOrDefaultAsync<DeliveryDetailRow>(
            sql,
            new { request.OrderId });

        if (row is null)
        {
            return Result.Failure<DeliveryResponse>(DeliveryErrors.NotFoundForOrder(request.OrderId));
        }

        Result access = DeliveryAccess.EnsureCanView(row.CustomerId, row.DriverId, deliveryContext);

        if (access.IsFailure)
        {
            return Result.Failure<DeliveryResponse>(access.Error);
        }

        DriverLocation? currentLocation = null;

        if (row.DriverId is not null && row.Status is DeliveryStatus.Assigned or DeliveryStatus.PickedUp)
        {
            currentLocation = await driverLocationStore.GetCurrentAsync(row.DriverId.Value, cancellationToken);
        }

        return row.ToResponse(currentLocation);
    }
}
