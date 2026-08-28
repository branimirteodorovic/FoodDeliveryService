using System.Diagnostics.Metrics;
using FoodDeliveryService.Common.Application.Diagnostics;
using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Diagnostics;

/// <summary>
/// The Support module's telemetry surface, on the shared <see cref="AppDiagnostics"/> convention —
/// the same shape as <c>OrdersDiagnostics</c> and <c>DeliveryDiagnostics</c>, wired by the single
/// <c>AddModuleDiagnostics(Name)</c> call in <c>Support.Api/Program.cs</c>. An unregistered meter
/// never errors, it just records into nothing, which is why the name lives here and not in the host.
/// <para>
/// Both instruments are recorded from domain-event handlers rather than from command handlers: that
/// is the path every state change already takes through the outbox, and the idempotent wrapper
/// means a redelivered message cannot double-count. Recording is always the LAST thing a handler
/// does, so a handler that throws and is retried whole does not inflate the series.
/// </para>
/// <para>
/// Only the two transitions that exist end-to-end in this milestone are instrumented. The rest of
/// the lifecycle gets its own measurements when the agent workflow that drives it ships — a counter
/// for a transition nothing can currently perform would read as a permanently flat line rather than
/// as an absence.
/// </para>
/// </summary>
public static class SupportDiagnostics
{
    public const string Name = "FoodDeliveryService.Support";

    private const string CategoryTagName = "category";

    private static readonly AppDiagnostics Diagnostics = new(Name);

    private static readonly Counter<long> Opened = Diagnostics.Meter.CreateCounter<long>(
        "support.tickets.opened",
        unit: "{ticket}",
        description: "Support tickets opened, tagged with the category the customer chose.");

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
    /// Time-to-resolution, straight off the resolved event, which carries both timestamps. A
    /// histogram rather than an average, because the number support actually cares about is the
    /// tail: the mean stays flat while the worst decile doubles.
    /// </summary>
    public static void RecordResolution(TicketCategory category, TimeSpan duration) =>
        ResolutionDuration.Record(
            duration.TotalSeconds,
            new KeyValuePair<string, object?>(CategoryTagName, category.ToString()));
}
