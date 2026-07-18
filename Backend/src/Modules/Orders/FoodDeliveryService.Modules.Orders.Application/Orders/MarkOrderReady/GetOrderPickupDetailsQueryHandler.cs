using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Domain.Orders;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.MarkOrderReady;

// The pickup coordinates come from the local Restaurant replica (hard rule #5 — Orders never
// queries the Restaurants database). The join is inner: an order can only exist for a replicated
// restaurant, so a missing row is a genuine failure the outbox should retry.
internal sealed class GetOrderPickupDetailsQueryHandler(IDbConnectionFactory dbConnectionFactory)
    : IQueryHandler<GetOrderPickupDetailsQuery, OrderPickupDetailsResponse>
{
    public async Task<Result<OrderPickupDetailsResponse>> Handle(
        GetOrderPickupDetailsQuery request,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        const string sql =
            $"""
             SELECT
                 o.id AS {nameof(OrderPickupDetailsResponse.OrderId)},
                 o.customer_id AS {nameof(OrderPickupDetailsResponse.CustomerId)},
                 o.restaurant_id AS {nameof(OrderPickupDetailsResponse.RestaurantId)},
                 r.latitude AS {nameof(OrderPickupDetailsResponse.RestaurantLatitude)},
                 r.longitude AS {nameof(OrderPickupDetailsResponse.RestaurantLongitude)},
                 o.delivery_street AS {nameof(OrderPickupDetailsResponse.DeliveryStreet)},
                 o.delivery_city AS {nameof(OrderPickupDetailsResponse.DeliveryCity)},
                 o.delivery_postal_code AS {nameof(OrderPickupDetailsResponse.DeliveryPostalCode)},
                 o.delivery_country AS {nameof(OrderPickupDetailsResponse.DeliveryCountry)},
                 o.delivery_notes AS {nameof(OrderPickupDetailsResponse.DeliveryNotes)},
                 o.delivery_latitude AS {nameof(OrderPickupDetailsResponse.DeliveryLatitude)},
                 o.delivery_longitude AS {nameof(OrderPickupDetailsResponse.DeliveryLongitude)},
                 o.subtotal AS {nameof(OrderPickupDetailsResponse.Subtotal)},
                 o.placed_on_utc AS {nameof(OrderPickupDetailsResponse.PlacedOnUtc)}
             FROM orders o
             INNER JOIN restaurants r ON r.id = o.restaurant_id
             WHERE o.id = @OrderId
             """;

        OrderPickupDetailsResponse? response =
            await connection.QuerySingleOrDefaultAsync<OrderPickupDetailsResponse>(sql, request);

        if (response is null)
        {
            return Result.Failure<OrderPickupDetailsResponse>(OrderErrors.NotFound(request.OrderId));
        }

        return response;
    }
}
