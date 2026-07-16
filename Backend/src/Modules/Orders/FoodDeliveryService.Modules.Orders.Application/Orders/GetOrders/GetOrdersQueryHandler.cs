using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Authentication;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.GetOrders;

// Scopes the list to the caller: a customer sees the orders they placed, a manager sees the orders
// for restaurants they manage (via the local Restaurant replica), an administrator sees all.
internal sealed class GetOrdersQueryHandler(
    IDbConnectionFactory dbConnectionFactory,
    IOrdersContext ordersContext)
    : IQueryHandler<GetOrdersQuery, IReadOnlyCollection<OrderSummaryResponse>>
{
    public async Task<Result<IReadOnlyCollection<OrderSummaryResponse>>> Handle(
        GetOrdersQuery request,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        const string sql =
            $"""
             SELECT
                 o.id AS {nameof(OrderSummaryResponse.Id)},
                 o.customer_id AS {nameof(OrderSummaryResponse.CustomerId)},
                 o.restaurant_id AS {nameof(OrderSummaryResponse.RestaurantId)},
                 o.status AS {nameof(OrderSummaryResponse.Status)},
                 o.subtotal AS {nameof(OrderSummaryResponse.Subtotal)},
                 o.payment_method AS {nameof(OrderSummaryResponse.PaymentMethod)},
                 o.placed_on_utc AS {nameof(OrderSummaryResponse.PlacedOnUtc)}
             FROM orders o
             LEFT JOIN restaurants r ON r.id = o.restaurant_id
             WHERE @IsAdmin OR o.customer_id = @UserId OR r.manager_user_id = @UserId
             ORDER BY o.placed_on_utc DESC
             LIMIT @Take OFFSET @Skip
             """;

        IEnumerable<OrderSummaryResponse> orders = await connection.QueryAsync<OrderSummaryResponse>(
            sql,
            new
            {
                ordersContext.UserId,
                IsAdmin = ordersContext.HasPermission(Permissions.Administer),
                Take = request.PageSize,
                Skip = (request.Page - 1) * request.PageSize
            });

        return orders.ToList();
    }
}
