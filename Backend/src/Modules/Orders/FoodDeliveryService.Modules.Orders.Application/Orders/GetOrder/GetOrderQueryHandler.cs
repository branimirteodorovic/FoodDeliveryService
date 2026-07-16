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
                 r.manager_user_id AS {nameof(OrderHeader.ManagerUserId)},
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
             WHERE o.id = @OrderId;

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

        await using var reader = await connection.QueryMultipleAsync(sql, new { request.OrderId });

        OrderHeader? header = await reader.ReadSingleOrDefaultAsync<OrderHeader>();

        if (header is null)
        {
            return Result.Failure<OrderResponse>(OrderErrors.NotFound(request.OrderId));
        }

        if (!CanRead(header))
        {
            return Result.Failure<OrderResponse>(OrderErrors.NotOwner);
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

    private bool CanRead(OrderHeader header) =>
        header.CustomerId == ordersContext.UserId ||
        header.ManagerUserId == ordersContext.UserId ||
        ordersContext.HasPermission(Permissions.Administer);

    // Row-mapping shape only — carries the restaurant manager id for the ownership check, which is
    // never surfaced in the response DTO.
    private sealed record OrderHeader(
        Guid Id,
        Guid CustomerId,
        Guid RestaurantId,
        Guid? ManagerUserId,
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
