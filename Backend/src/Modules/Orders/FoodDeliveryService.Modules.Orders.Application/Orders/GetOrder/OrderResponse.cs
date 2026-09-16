using FoodDeliveryService.Modules.Orders.Domain.Orders;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.GetOrder;

// Response DTO — domain entities are never exposed in API responses.
public sealed record OrderResponse(
    Guid Id,
    Guid CustomerId,
    Guid RestaurantId,
    OrderStatus Status,
    PaymentMethod PaymentMethod,
    // Feature 3.8 Milestone F. A second, independent dimension beside Status (§1.2) — NotRequired
    // for the whole life of a cash order.
    PaymentStatus PaymentStatus,
    decimal Subtotal,
    decimal CommissionRate,
    string Street,
    string City,
    string PostalCode,
    string Country,
    string? Notes,
    DateTime PlacedOnUtc,
    IReadOnlyCollection<OrderItemResponse> Items);

public sealed record OrderItemResponse(
    Guid Id,
    Guid MenuItemId,
    string Name,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);
