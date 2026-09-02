using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Orders.Domain.Orders;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.GetOrder;

// Access is ownership-scoped: the customer who placed the order, the manager of its restaurant, or
// an administrator. The manager id is read from the local Restaurant replica (hard rule #5 — Orders
// never queries the Restaurants database).
//
// The scope is a WHERE clause rather than a branch after the read, so a caller who is none of the
// three gets no row and therefore the 404 — the platform's convention is 404, not 403, when the
// resource is not the caller's, because a distinguishable "not yours" confirms the id exists.
internal sealed class GetOrderQueryHandler(
    IDbConnectionFactory dbConnectionFactory,
    IOrdersContext ordersContext)
    : IQueryHandler<GetOrderQuery, OrderResponse>
{
    public async Task<Result<OrderResponse>> Handle(GetOrderQuery request, CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        const string sql =
            $"""
             SELECT
                 o.id AS {nameof(OrderHeader.Id)},
                 o.customer_id AS {nameof(OrderHeader.CustomerId)},
                 o.restaurant_id AS {nameof(OrderHeader.RestaurantId)},
                 o.status AS {nameof(OrderHeader.Status)},
                 o.payment_method AS {nameof(OrderHeader.PaymentMethod)},
                 o.subtotal AS {nameof(OrderHeader.Subtotal)},
                 o.commission_rate AS {nameof(OrderHeader.CommissionRate)},
                 o.delivery_street AS {nameof(OrderHeader.Street)},
                 o.delivery_city AS {nameof(OrderHeader.City)},
                 o.delivery_postal_code AS {nameof(OrderHeader.PostalCode)},
                 o.delivery_country AS {nameof(OrderHeader.Country)},
                 o.delivery_notes AS {nameof(OrderHeader.Notes)},
                 o.placed_on_utc AS {nameof(OrderHeader.PlacedOnUtc)}
             FROM orders o
             LEFT JOIN restaurants r ON r.id = o.restaurant_id
             WHERE o.id = @OrderId
               AND (@IsAdmin OR o.customer_id = @UserId OR r.manager_user_id = @UserId);

             SELECT
                 id AS {nameof(OrderItemResponse.Id)},
                 menu_item_id AS {nameof(OrderItemResponse.MenuItemId)},
                 name AS {nameof(OrderItemResponse.Name)},
                 unit_price AS {nameof(OrderItemResponse.UnitPrice)},
                 quantity AS {nameof(OrderItemResponse.Quantity)},
                 line_total AS {nameof(OrderItemResponse.LineTotal)}
             FROM order_items
             WHERE order_id = @OrderId
             ORDER BY name;
             """;

        // The items query is unconditional, but it is keyed on the same order id the header query
        // scoped — a caller who cannot read the header never reaches the second grid.
        await using var reader = await connection.QueryMultipleAsync(
            sql,
            new
            {
                request.OrderId,
                ordersContext.UserId,
                IsAdmin = ordersContext.HasPermission(Permissions.Administer)
            });

        OrderHeader? header = await reader.ReadSingleOrDefaultAsync<OrderHeader>();

        if (header is null)
        {
            return Result.Failure<OrderResponse>(OrderErrors.NotFound(request.OrderId));
        }

        var items = (await reader.ReadAsync<OrderItemResponse>()).ToList();

        return new OrderResponse(
            header.Id,
            header.CustomerId,
            header.RestaurantId,
            header.Status,
            header.PaymentMethod,
            header.Subtotal,
            header.CommissionRate,
            header.Street,
            header.City,
            header.PostalCode,
            header.Country,
            header.Notes,
            header.PlacedOnUtc,
            items);
    }

    // Row-mapping shape only — never surfaced as-is in the response DTO.
    private sealed record OrderHeader(
        Guid Id,
        Guid CustomerId,
        Guid RestaurantId,
        OrderStatus Status,
        PaymentMethod PaymentMethod,
        decimal Subtotal,
        decimal CommissionRate,
        string Street,
        string City,
        string PostalCode,
        string Country,
        string? Notes,
        DateTime PlacedOnUtc);
}
