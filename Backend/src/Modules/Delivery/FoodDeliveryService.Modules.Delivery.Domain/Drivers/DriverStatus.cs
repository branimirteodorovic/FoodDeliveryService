namespace FoodDeliveryService.Modules.Delivery.Domain.Drivers;

/// <summary>
/// A driver's availability for delivery work. Onboarding starts them Offline; the availability
/// transitions (GoAvailable/GoOffline/Reserve/Release) land in Milestone C of the Delivery plan.
/// </summary>
public enum DriverStatus
{
    Offline = 1,
    Available = 2,
    Busy = 3
}
