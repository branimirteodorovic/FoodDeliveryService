using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.FraudDetection.Domain.Drivers;

/// <summary>
/// What one driver has been doing, accumulated from the Delivery service's events. Keyed by the
/// Users service's UserId (which is what Delivery puts on <c>DriverId</c>).
/// <para>
/// A behavioural projection with the same contract as <see cref="Customers.CustomerBehaviour"/>:
/// counter updates only, no domain events, created by whichever event about this driver arrives
/// first.
/// </para>
/// </summary>
public sealed class DriverBehaviour : Entity
{
    private DriverBehaviour()
    {
    }

    public Guid Id { get; private set; }

    public DateTime FirstSeenOnUtc { get; private set; }

    public int PickupsCompleted { get; private set; }

    public int DeliveriesCompleted { get; private set; }

    /// <summary>Offers this driver was made and actively declined (not offers that merely lapsed).</summary>
    public int OffersRejected { get; private set; }

    /// <summary>
    /// Deliveries marked delivered from further than the configured radius from the drop-off.
    /// Nothing writes this yet — the position trail it needs is Milestone D, which is the milestone
    /// that adds the only caller of <see cref="RecordLocationMismatch"/>.
    /// </summary>
    public int LocationMismatches { get; private set; }

    public DateTime? LastDeliveryOnUtc { get; private set; }

    public static DriverBehaviour Create(Guid driverId, DateTime firstSeenOnUtc)
    {
        return new DriverBehaviour
        {
            Id = driverId,
            FirstSeenOnUtc = firstSeenOnUtc
        };
    }

    public void RecordPickup()
    {
        PickupsCompleted++;
    }

    public void RecordDeliveryCompleted(DateTime deliveredOnUtc)
    {
        DeliveriesCompleted++;

        if (LastDeliveryOnUtc is null || deliveredOnUtc > LastDeliveryOnUtc)
        {
            LastDeliveryOnUtc = deliveredOnUtc;
        }
    }

    public void RecordOfferRejected()
    {
        OffersRejected++;
    }

    public void RecordLocationMismatch()
    {
        LocationMismatches++;
    }
}
