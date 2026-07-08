namespace FoodDeliveryService.Modules.Orders.Domain.Orders;

// Owned value object (EF OwnsOne on Order) — snapshotted at placement so later profile/address
// changes never rewrite where an already-placed order is going.
public sealed record DeliveryAddress(
    string Street,
    string City,
    string PostalCode,
    string Country,
    string? Notes);
