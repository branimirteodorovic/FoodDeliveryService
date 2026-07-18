namespace FoodDeliveryService.Modules.Delivery.Domain.Drivers;

/// <summary>
/// A driver's availability for delivery work. Onboarding starts them Offline. Only an Available
/// driver who is also reporting a position is an assignment candidate; Reserve/Release move them
/// in and out of Busy around a delivery.
/// </summary>
public enum DriverStatus
{
    Offline = 1,
    Available = 2,
    Busy = 3
}
