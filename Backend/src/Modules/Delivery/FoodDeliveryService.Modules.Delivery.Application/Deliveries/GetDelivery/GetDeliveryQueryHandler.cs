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

        const string sql =
            $"""
             {DeliveryDetailRow.SelectSql}
             WHERE d.id = @DeliveryId
             """;

        DeliveryDetailRow? row = await connection.QuerySingleOrDefaultAsync<DeliveryDetailRow>(
            sql,
            new { request.DeliveryId });

        if (row is null)
        {
            return Result.Failure<DeliveryResponse>(DeliveryErrors.NotFound(request.DeliveryId));
        }

        Result access = DeliveryAccess.EnsureCanView(row.CustomerId, row.DriverId, deliveryContext);

        if (access.IsFailure)
        {
            return Result.Failure<DeliveryResponse>(access.Error);
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
