using FoodDeliveryService.Modules.Orders.Domain.Orders;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.GetOrders;

public sealed record OrderSummaryResponse(
    Guid Id,
    Guid CustomerId,
    Guid RestaurantId,
    OrderStatus Status,
    decimal Subtotal,
    PaymentMethod PaymentMethod,
    DateTime PlacedOnUtc);
