using System.Diagnostics.Metrics;
using FoodDeliveryService.Modules.Delivery.Infrastructure.Locations;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Assignment;

/// <summary>
/// The assignment instruments, created on <c>DeliveryDiagnostics.Meter</c> — the module's one
/// registered meter — so they need no host change of their own. They live next to the routine that
/// records them rather than on the holder, which stays the module's telemetry surface and not a
/// catalogue of every instrument in it.
/// <para>
/// <b>The two instruments do not share a denominator.</b> The histogram measures one turn of
/// <c>DeliveryAssignmentService.OfferNextAsync</c>; the counter also carries
/// <see cref="DeliveryAssignmentOutcome.Expired"/>, which is emitted by the expiry job for a
/// PREVIOUS offer that lapsed. Read the counter per outcome, never as a total.
/// </para>
/// </summary>
internal static class DeliveryAssignmentDiagnostics
{
    private const string OutcomeTagName = "outcome";

    private static readonly Counter<long> Outcomes = DeliveryDiagnostics.Meter.CreateCounter<long>(
        "delivery.assignment.outcome",
        unit: "{assignment}",
        description: "How each turn of the driver-offer cycle ended.");

    private static readonly Histogram<double> Duration = DeliveryDiagnostics.Meter.CreateHistogram<double>(
        "delivery.assignment.duration",
        unit: "s",
        description: "Time spent finding and offering a driver for one delivery.");

    /// <summary>
    /// One turn of the offer routine: the geo candidate search, the per-candidate re-verification
    /// and the save. Tagged by outcome because a <c>lock_contended</c> turn returns in microseconds
    /// and would otherwise drag the p50 of the real work down with it.
    /// </summary>
    public static void RecordAttempt(DeliveryAssignmentOutcome outcome, double durationInSeconds)
    {
        var tag = OutcomeTag(outcome);

        Outcomes.Add(1, tag);
        Duration.Record(durationInSeconds, tag);
    }

    /// <summary>
    /// An offer window lapsed. Counted at detection — the SELECT that finds an <c>Offered</c>
    /// delivery past its deadline IS the definition of a lapsed offer, so a delivery that races to
    /// another state before the expiry command lands is still honestly counted.
    /// </summary>
    public static void RecordExpiredOffer() =>
        Outcomes.Add(1, OutcomeTag(DeliveryAssignmentOutcome.Expired));

    /// <summary>
    /// <c>NoDriver</c> → <c>no_driver</c>: snake_case tag values, matching <c>cache.key_prefix</c>
    /// and the OpenTelemetry convention, rather than the enum's PascalCase.
    /// </summary>
    private static KeyValuePair<string, object?> OutcomeTag(DeliveryAssignmentOutcome outcome) =>
        new(OutcomeTagName, outcome switch
        {
            DeliveryAssignmentOutcome.Offered => "offered",
            DeliveryAssignmentOutcome.NoDriver => "no_driver",
            DeliveryAssignmentOutcome.LockContended => "lock_contended",
            DeliveryAssignmentOutcome.NotPending => "not_pending",
            DeliveryAssignmentOutcome.Expired => "expired",
            _ => "failed"
        });
}
