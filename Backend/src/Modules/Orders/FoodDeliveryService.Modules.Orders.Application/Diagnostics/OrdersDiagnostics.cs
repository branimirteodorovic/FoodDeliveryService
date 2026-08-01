using System.Diagnostics.Metrics;
using FoodDeliveryService.Common.Application.Diagnostics;
using FoodDeliveryService.Modules.Orders.Domain.Orders;

namespace FoodDeliveryService.Modules.Orders.Application.Diagnostics;

/// <summary>
/// The Orders module's telemetry surface, on the shared <see cref="AppDiagnostics"/> convention —
/// the same shape as <c>DeliveryDiagnostics</c> and <c>RealTimeDiagnostics</c>, wired by the single
/// <c>AddModuleDiagnostics(Name)</c> call in <c>Orders.Api/Program.cs</c>.
/// <para>
/// It carries the platform's business metrics: how many orders are being placed, and how orders move
/// through the lifecycle. Every measurement is recorded from a domain-event handler — the outbox
/// path each transition already takes — so no command handler and no aggregate knows metrics exist,
/// and the idempotent outbox wrapper means a redelivered message does not double-count.
/// </para>
/// <para>
/// It declares no <c>ActivitySource</c> of its own: Orders has no operation the ASP.NET Core, EF Core
/// and MassTransit instrumentation don't already span. The source exists on the underlying
/// <see cref="AppDiagnostics"/> and is registered along with the meter, so adding one later is a
/// property, not a host change.
/// </para>
/// </summary>
public static class OrdersDiagnostics
{
    public const string Name = "FoodDeliveryService.Orders";

    /// <summary>
    /// The status an order came from. <c>none</c> for placement, which starts the lifecycle rather
    /// than moving through it — an empty tag value would be indistinguishable from a bug.
    /// </summary>
    private const string FromTagName = "from";

    private const string ToTagName = "to";

    private const string NoPreviousStatus = "none";

    private static readonly AppDiagnostics Diagnostics = new(Name);

    private static readonly Counter<long> Placed = Diagnostics.Meter.CreateCounter<long>(
        "orders.placed",
        unit: "{order}",
        description: "Orders successfully placed by a customer.");

    private static readonly Counter<long> Transitions = Diagnostics.Meter.CreateCounter<long>(
        "orders.state_transition",
        unit: "{transition}",
        description: "Order lifecycle transitions, tagged with the statuses moved between.");

    public static Meter Meter => Diagnostics.Meter;

    /// <summary>Orders per minute — the single headline number for the business dashboard.</summary>
    public static void RecordPlaced() => Placed.Add(1);

    /// <summary>
    /// One measurement per lifecycle transition. Tag cardinality is the square of an eight-value
    /// enum at worst and seven pairs in practice, so the whole transition graph costs less than a
    /// single id-tagged series would.
    /// </summary>
    public static void RecordTransition(OrderStatus? from, OrderStatus to) =>
        Transitions.Add(
            1,
            new KeyValuePair<string, object?>(FromTagName, from?.ToString() ?? NoPreviousStatus),
            new KeyValuePair<string, object?>(ToTagName, to.ToString()));
}
