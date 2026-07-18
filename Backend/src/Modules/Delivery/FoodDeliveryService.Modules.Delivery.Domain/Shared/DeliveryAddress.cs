namespace FoodDeliveryService.Modules.Delivery.Domain.Shared;

// Owned value object — the dropoff address snapshotted from OrderReadyForPickup. Carries the
// coordinates the driver navigates to. Coordinates are non-null: they are validated required at
// order placement, so a replicated order always has them. Reused by the Delivery aggregate's
// DropoffAddress in Milestone E.
public sealed record DeliveryAddress(
    string Street,
    string City,
    string PostalCode,
    string Country,
    string? Notes,
    double Latitude,
    double Longitude);
