namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Locations;

internal sealed class DriverLocationStoreOptions
{
    /// <summary>
    /// How long a reported position stays "fresh". Redis GEO members have no per-entry TTL, so a
    /// crashed driver would otherwise linger in the pool at their last position forever. The
    /// position hash carries this TTL; a candidate whose hash has expired is dropped from the
    /// search even though the geo entry remains. Reports arrive every few seconds, so 60s tolerates
    /// a handful of missed beats before a driver is treated as gone.
    /// </summary>
    public int LocationTtlInSeconds { get; init; } = 60;
}
