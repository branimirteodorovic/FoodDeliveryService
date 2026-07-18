namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Assignment;

/// <summary>
/// Bound from the "Delivery:Assignment" section. Options rather than constants on purpose — the
/// integration tests shrink the offer window and job interval to keep the timeout path fast.
/// </summary>
internal sealed class DeliveryAssignmentOptions
{
    /// <summary>How long an offered driver has to accept before the offer lapses.</summary>
    public int OfferWindowInSeconds { get; init; } = 30;

    /// <summary>Radius around the restaurant searched for available drivers.</summary>
    public double SearchRadiusKm { get; init; } = 5;

    /// <summary>Max candidates pulled per search — the offer routine walks them nearest-first.</summary>
    public int CandidateLimit { get; init; } = 10;

    /// <summary>
    /// ProcessExpiredOffersJob poll interval. Bounds how long past the window an offer can linger:
    /// the effective window is [OfferWindow, OfferWindow + this].
    /// </summary>
    public int ExpiredOffersJobIntervalInSeconds { get; init; } = 5;
}
