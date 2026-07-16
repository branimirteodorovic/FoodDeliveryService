using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.GetOrders;

public sealed record GetOrdersQuery(int Page, int PageSize) : IQuery<IReadOnlyCollection<OrderSummaryResponse>>;
