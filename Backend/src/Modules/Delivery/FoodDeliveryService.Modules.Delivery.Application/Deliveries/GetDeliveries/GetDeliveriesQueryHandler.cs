using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Authentication;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetDeliveries;

// Scopes the list to the caller: a driver sees the deliveries assigned to them; an administrator
// (deliveries:administer) sees all.
internal sealed class GetDeliveriesQueryHandler(
    IDbConnectionFactory dbConnectionFactory,
    IDeliveryContext deliveryContext)
    : IQueryHandler<GetDeliveriesQuery, IReadOnlyCollection<DeliverySummaryResponse>>
{
    public async Task<Result<IReadOnlyCollection<DeliverySummaryResponse>>> Handle(
        GetDeliveriesQuery request,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        const string sql =
            $"""
             SELECT
                 d.id AS {nameof(DeliverySummaryResponse.Id)},
                 d.order_id AS {nameof(DeliverySummaryResponse.OrderId)},
                 d.restaurant_id AS {nameof(DeliverySummaryResponse.RestaurantId)},
                 d.customer_id AS {nameof(DeliverySummaryResponse.CustomerId)},
                 d.status AS {nameof(DeliverySummaryResponse.Status)},
                 d.driver_id AS {nameof(DeliverySummaryResponse.DriverId)},
                 d.assigned_on_utc AS {nameof(DeliverySummaryResponse.AssignedOnUtc)},
                 d.picked_up_on_utc AS {nameof(DeliverySummaryResponse.PickedUpOnUtc)},
                 d.delivered_on_utc AS {nameof(DeliverySummaryResponse.DeliveredOnUtc)},
                 d.created_on_utc AS {nameof(DeliverySummaryResponse.CreatedOnUtc)}
             FROM deliveries d
             WHERE @IsAdmin OR d.driver_id = @UserId
             ORDER BY d.created_on_utc DESC
             LIMIT @Take OFFSET @Skip
             """;

        IEnumerable<DeliverySummaryResponse> deliveries = await connection.QueryAsync<DeliverySummaryResponse>(
            sql,
            new
            {
                deliveryContext.UserId,
                IsAdmin = deliveryContext.HasPermission(Permissions.AdministerDeliveries),
                Take = request.PageSize,
                Skip = (request.Page - 1) * request.PageSize
            });

        return deliveries.ToList();
    }
}
