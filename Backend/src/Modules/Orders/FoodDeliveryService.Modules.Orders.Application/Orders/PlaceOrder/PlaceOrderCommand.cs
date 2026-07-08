using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.PlaceOrder;

// Items carry ids + quantities only — prices are authoritative from the MenuItem replica, never
// the client. PaymentMethod arrives as text and is validated against the enum; IdempotencyKey is
// the client's Idempotency-Key header.
public sealed record PlaceOrderCommand(
    Guid RestaurantId,
    IReadOnlyCollection<PlaceOrderItem> Items,
    string Street,
    string City,
    string PostalCode,
    string Country,
    string? Notes,
    string PaymentMethod,
    string IdempotencyKey) : ICommand<Guid>;

public sealed record PlaceOrderItem(Guid MenuItemId, int Quantity);
