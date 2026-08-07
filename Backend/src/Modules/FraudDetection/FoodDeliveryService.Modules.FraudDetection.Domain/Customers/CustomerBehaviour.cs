using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.FraudDetection.Domain.Shared;

namespace FoodDeliveryService.Modules.FraudDetection.Domain.Customers;

/// <summary>
/// What one customer has been doing, accumulated from the order lifecycle events FraudDetection consumes.
/// Keyed by the Users service's UserId.
/// <para>
/// This is a <b>behavioural projection</b>, not an aggregate: every method is an idempotent-by-
/// construction counter update driven by an inbox message, and none of them raise domain events —
/// nothing outside FraudDetection cares that a counter moved. The alert that a human eventually sees is a
/// separate aggregate (Milestone C); this row only holds the numbers the signals read.
/// </para>
/// <para>
/// Row creation is decoupled from registration on purpose. <see cref="Create"/> is called by
/// whichever event about this customer arrives first — a registration or an order — because the
/// events come from two different services over two different queues and neither ordering is
/// guaranteed. <see cref="RegisteredOnUtc"/> therefore stays null until the Users event lands, and
/// the account-age signals in Milestone E treat "unknown age" as "no signal" rather than "new".
/// </para>
/// </summary>
public sealed class CustomerBehaviour : Entity
{
    private CustomerBehaviour()
    {
    }

    /// <summary>The Users service's UserId — never generated locally.</summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Account creation, from UserRegistered. Null until that event is consumed (see the class
    /// remarks) — an unknown account age must never be read as a young one.
    /// </summary>
    public DateTime? RegisteredOnUtc { get; private set; }

    /// <summary>When FraudDetection first heard about this customer at all — the floor on the account age.</summary>
    public DateTime FirstSeenOnUtc { get; private set; }

    public int OrdersPlaced { get; private set; }

    public int OrdersCancelled { get; private set; }

    /// <summary>
    /// Cancellations that happened after the restaurant accepted the order but before a driver
    /// picked it up — the promotion-abuse shape the Milestone B signal looks for. Derived from the
    /// order's status at cancellation, which FraudDetection knows from its own <see cref="Orders.OrderFact"/>.
    /// </summary>
    public int CancelledBeforePickup { get; private set; }

    public int OrdersRejected { get; private set; }

    public int OrdersDelivered { get; private set; }

    public decimal TotalOrderValue { get; private set; }

    public DateTime? LastOrderOnUtc { get; private set; }

    /// <summary>Start of the current counter window. See <see cref="BehaviourWindow"/>.</summary>
    public DateTime WindowStartedOnUtc { get; private set; }

    public int OrdersPlacedInWindow { get; private set; }

    public int OrdersCancelledInWindow { get; private set; }

    public static CustomerBehaviour Create(Guid customerId, DateTime firstSeenOnUtc)
    {
        return new CustomerBehaviour
        {
            Id = customerId,
            FirstSeenOnUtc = firstSeenOnUtc,
            WindowStartedOnUtc = firstSeenOnUtc
        };
    }

    /// <summary>
    /// Records the account-creation timestamp from UserRegistered. Idempotent: a redelivery cannot
    /// move a date that is already known, and the first-seen floor only ever moves earlier.
    /// </summary>
    public void Register(DateTime registeredOnUtc)
    {
        RegisteredOnUtc ??= registeredOnUtc;

        if (registeredOnUtc < FirstSeenOnUtc)
        {
            FirstSeenOnUtc = registeredOnUtc;
        }
    }

    public void RecordOrderPlaced(decimal subtotal, DateTime placedOnUtc, TimeSpan window)
    {
        RollWindow(placedOnUtc, window);

        OrdersPlaced++;
        OrdersPlacedInWindow++;
        TotalOrderValue += subtotal;

        if (LastOrderOnUtc is null || placedOnUtc > LastOrderOnUtc)
        {
            LastOrderOnUtc = placedOnUtc;
        }
    }

    public void RecordOrderCancelled(DateTime cancelledOnUtc, bool beforePickup, TimeSpan window)
    {
        RollWindow(cancelledOnUtc, window);

        OrdersCancelled++;
        OrdersCancelledInWindow++;

        if (beforePickup)
        {
            CancelledBeforePickup++;
        }
    }

    public void RecordOrderRejected()
    {
        OrdersRejected++;
    }

    public void RecordOrderDelivered()
    {
        OrdersDelivered++;
    }

    /// <summary>
    /// Opens a fresh window if the current one has aged out. An event that predates the current
    /// window (a late redelivery) is counted into it rather than reopening a closed window —
    /// over-counting a stale event is cheaper than resetting a live customer's rate to zero.
    /// </summary>
    private void RollWindow(DateTime occurredOnUtc, TimeSpan window)
    {
        if (occurredOnUtc - WindowStartedOnUtc <= window)
        {
            return;
        }

        WindowStartedOnUtc = occurredOnUtc;
        OrdersPlacedInWindow = 0;
        OrdersCancelledInWindow = 0;
    }
}
