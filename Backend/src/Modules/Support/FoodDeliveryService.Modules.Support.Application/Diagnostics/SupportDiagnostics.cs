using System.Diagnostics.Metrics;
using FoodDeliveryService.Common.Application.Diagnostics;
using FoodDeliveryService.Modules.Support.Domain.Refunds;
using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Diagnostics;

/// <summary>
/// The Support module's telemetry surface, on the shared <see cref="AppDiagnostics"/> convention —
/// the same shape as <c>OrdersDiagnostics</c> and <c>DeliveryDiagnostics</c>, wired by the single
/// <c>AddModuleDiagnostics(Name)</c> call in <c>Support.Api/Program.cs</c>. An unregistered meter
/// never errors, it just records into nothing, which is why the name lives here and not in the host.
/// <para>
/// Every instrument is recorded from a domain-event handler rather than from a command handler: that
/// is the path every state change already takes through the outbox, and the idempotent wrapper
/// means a redelivered message cannot double-count. Recording is always the LAST thing a handler
/// does, so a handler that throws and is retried whole does not inflate the series.
/// </para>
/// <para>
/// The four instruments cover the two questions a support organisation is actually run on: how much
/// work is arriving (opened, by category), and how it is being got through (the transition graph,
/// time-to-resolution, and what happens to the refunds agents ask for). Every tag value is an enum
/// member — never a ticket id, an agent id or a customer's free text, all of which would turn one
/// series into one series per case.
/// </para>
/// </summary>
public static class SupportDiagnostics
{
    public const string Name = "FoodDeliveryService.Support";

    private const string CategoryTagName = "category";

    /// <summary>The status a ticket moved out of. Always present — every transition has a source.</summary>
    private const string FromTagName = "from";

    private const string ToTagName = "to";

    private const string OutcomeTagName = "outcome";

    private static readonly AppDiagnostics Diagnostics = new(Name);

    private static readonly Counter<long> Opened = Diagnostics.Meter.CreateCounter<long>(
        "support.tickets.opened",
        unit: "{ticket}",
        description: "Support tickets opened, tagged with the category the customer chose.");

    private static readonly Counter<long> Transitions = Diagnostics.Meter.CreateCounter<long>(
        "support.tickets.transition",
        unit: "{transition}",
        description: "Ticket lifecycle transitions, tagged with the statuses moved between.");

    private static readonly Counter<long> RefundsDecided = Diagnostics.Meter.CreateCounter<long>(
        "support.refunds.decided",
        unit: "{decision}",
        description: "Refund requests an administrator decided, tagged approved or rejected.");

    private static readonly Histogram<double> ResolutionDuration = Diagnostics.Meter.CreateHistogram<double>(
        "support.tickets.resolution.duration",
        unit: "s",
        description: "Wall-clock time from a ticket being opened to being resolved.");

    public static Meter Meter => Diagnostics.Meter;

    /// <summary>
    /// Tickets per category — the signal that reads a support queue as a product-quality measure
    /// rather than a staffing one. Bounded to the seven enum values; the customer's free-text
    /// subject never becomes a tag.
    /// </summary>
    public static void RecordOpened(TicketCategory category) =>
        Opened.Add(1, new KeyValuePair<string, object?>(CategoryTagName, category.ToString()));

    /// <summary>
    /// One measurement per lifecycle transition, the same shape as <c>orders.state_transition</c>.
    /// Tag cardinality is bounded by a five-value enum squared, and only seven of those pairs are
    /// reachable at all — the whole transition graph costs less than a single id-tagged series.
    /// <para>
    /// Deliberately no <c>none</c> source value: unlike an order, a ticket cannot be opened into
    /// the middle of its lifecycle, so the opening edge is <c>support.tickets.opened</c> and every
    /// measurement here genuinely has a status it came from.
    /// </para>
    /// </summary>
    public static void RecordTransition(TicketStatus from, TicketStatus to) =>
        Transitions.Add(
            1,
            new KeyValuePair<string, object?>(FromTagName, from.ToString()),
            new KeyValuePair<string, object?>(ToTagName, to.ToString()));

    /// <summary>
    /// Approvals against rejections. The rate between them is the signal a support manager reads —
    /// a queue approving everything is not applying a policy, and one approving nothing is not
    /// resolving anything. The amount is deliberately not a metric: it is money-shaped, it belongs
    /// in the analytics summary where it can be attributed, and a histogram of it would invite
    /// reading a Grafana panel as a ledger.
    /// </summary>
    public static void RecordRefundDecision(RefundStatus outcome) =>
        RefundsDecided.Add(1, new KeyValuePair<string, object?>(OutcomeTagName, outcome.ToString()));

    /// <summary>
    /// Time-to-resolution, straight off the resolved event, which carries both timestamps. A
    /// histogram rather than an average, because the number support actually cares about is the
    /// tail: the mean stays flat while the worst decile doubles.
    /// </summary>
    public static void RecordResolution(TicketCategory category, TimeSpan duration) =>
        ResolutionDuration.Record(
            duration.TotalSeconds,
            new KeyValuePair<string, object?>(CategoryTagName, category.ToString()));
}
