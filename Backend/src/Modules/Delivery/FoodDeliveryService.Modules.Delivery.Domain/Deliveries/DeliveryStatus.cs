namespace FoodDeliveryService.Modules.Delivery.Domain.Deliveries;

public enum DeliveryStatus
{
    /// <summary>Created from OrderReadyForPickup; waiting for the offer routine to pick a driver.</summary>
    Pending = 0,

    /// <summary>Offered to exactly one driver, who has until OfferExpiresOnUtc to accept.</summary>
    Offered = 1,

    /// <summary>A driver accepted and is heading to the restaurant.</summary>
    Assigned = 2,

    /// <summary>The assigned driver collected the food (Milestone F).</summary>
    PickedUp = 3,

    /// <summary>Terminal — the food arrived (Milestone F).</summary>
    Delivered = 4,

    /// <summary>Every candidate was tried without an accept — parked for admin/support re-offer.</summary>
    Unassigned = 5,

    /// <summary>Terminal — the order was cancelled mid-flight.</summary>
    Cancelled = 6
}
