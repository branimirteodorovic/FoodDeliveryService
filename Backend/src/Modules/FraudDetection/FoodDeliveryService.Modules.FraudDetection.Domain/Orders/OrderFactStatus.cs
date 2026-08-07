namespace FoodDeliveryService.Modules.FraudDetection.Domain.Orders;

/// <summary>
/// FraudDetection's own view of where an order got to. Deliberately a separate enum from the Orders module's
/// OrderStatus: it is reconstructed from the integration events FraudDetection happens to consume, so it is
/// coarser than the owning service's state machine and must never be mistaken for it (hard rule #4
/// — FraudDetection may not reference Orders' Domain).
/// </summary>
public enum OrderFactStatus
{
    Placed = 1,
    Accepted = 2,
    ReadyForPickup = 3,
    PickedUp = 4,
    Delivered = 5,
    Cancelled = 6,
    Rejected = 7
}
