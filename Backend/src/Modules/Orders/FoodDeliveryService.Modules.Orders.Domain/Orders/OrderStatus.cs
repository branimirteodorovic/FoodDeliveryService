namespace FoodDeliveryService.Modules.Orders.Domain.Orders;

// The full lifecycle is modeled so the state machine is complete and unit-testable, but
// OutForDelivery/Delivered are driven by the Delivery service in Phase 2 — no endpoints expose
// them this iteration.
public enum OrderStatus
{
    Pending = 1,
    Accepted = 2,
    Rejected = 3,
    Preparing = 4,
    ReadyForPickup = 5,
    OutForDelivery = 6,
    Delivered = 7,
    Cancelled = 8
}
