using System.Diagnostics.Metrics;
using FoodDeliveryService.Common.Application.Diagnostics;

namespace FoodDeliveryService.Modules.Payments.Application.Diagnostics;

/// <summary>
/// The Payments module's telemetry surface, on the shared <see cref="AppDiagnostics"/> convention —
/// the same shape as <c>OrdersDiagnostics</c> and <c>SupportDiagnostics</c>, wired by the single
/// <c>AddModuleDiagnostics(Name)</c> call in <c>Payments.Api/Program.cs</c>. An unregistered meter
/// never errors, it just records into nothing, which is why the name lives here and not in the host.
/// <para>
/// <b>This is the one service where "the panel is empty" and "no money moved" look identical.</b>
/// Every other module's business metrics describe work; these describe funds. An authorization rate
/// that falls to zero is either a quiet hour or a rejected API key, and reading
/// <c>payments.authorized</c> against <c>payments.failed</c> and <c>payments.gateway.duration</c> is
/// what tells those apart.
/// </para>
/// <para>
/// Four of the seven instruments are recorded from <b>domain-event handlers</b> — the outbox path
/// every state change already takes, wrapped idempotent, so a redelivered message cannot
/// double-count — and always as the last thing the handler does, so a handler that throws and is
/// retried whole does not inflate the series. The three exceptions are deliberate and documented on
/// their methods: <see cref="RecordReleased"/>, <see cref="RecordGatewayCall"/> and
/// <see cref="RecordWebhookLag"/>.
/// </para>
/// <para>
/// Every tag value is a bounded constant — an operation name, an outcome, one of
/// <c>PaymentFailureReason</c>'s six strings. Never a Stripe message, never an id, never an amount:
/// the first is unbounded third-party free text, the second turns one series into one series per
/// order, and the third would invite reading a Grafana panel as a ledger.
/// </para>
/// </summary>
public static class PaymentsDiagnostics
{
    public const string Name = "FoodDeliveryService.Payments";

    /// <summary>One of <c>PaymentFailureReason</c>'s six values — the string the event and the email carry too.</summary>
    private const string ReasonTagName = "reason";

    /// <summary>What ended the order: <c>rejected</c> or <c>cancelled</c>.</summary>
    private const string TriggerTagName = "trigger";

    /// <summary>The gateway verb — <c>authorize</c>, <c>capture</c>, <c>release</c>, <c>refund</c> and the card-management calls.</summary>
    private const string OperationTagName = "operation";

    private const string OutcomeTagName = "outcome";

    private const string EventTypeTagName = "event_type";

    private static readonly AppDiagnostics Diagnostics = new(Name);

    private static readonly Counter<long> Authorized = Diagnostics.Meter.CreateCounter<long>(
        "payments.authorized",
        unit: "{payment}",
        description: "Card authorizations placed when an order was placed.");

    private static readonly Counter<long> Captured = Diagnostics.Meter.CreateCounter<long>(
        "payments.captured",
        unit: "{payment}",
        description: "Authorizations captured — the point at which money actually moved.");

    private static readonly Counter<long> Failed = Diagnostics.Meter.CreateCounter<long>(
        "payments.failed",
        unit: "{payment}",
        description: "Authorizations that did not succeed, tagged with the bounded failure reason.");

    private static readonly Counter<long> Released = Diagnostics.Meter.CreateCounter<long>(
        "payments.released",
        unit: "{payment}",
        description: "Holds given up uncharged, tagged with what ended the order.");

    private static readonly Counter<long> RefundsSettled = Diagnostics.Meter.CreateCounter<long>(
        "refunds.settled",
        unit: "{refund}",
        description: "Refunds the provider accepted, closing an administrator's approval.");

    private static readonly Histogram<double> GatewayDuration = Diagnostics.Meter.CreateHistogram<double>(
        "payments.gateway.duration",
        unit: "s",
        description: "Time spent inside one call to the payment provider, by operation and outcome.");

    private static readonly Histogram<double> WebhookLag = Diagnostics.Meter.CreateHistogram<double>(
        "payments.webhook.lag",
        unit: "s",
        description: "Time from a provider event being recorded to the platform acting on it.");

    public static Meter Meter => Diagnostics.Meter;

    /// <summary>
    /// A hold placed, from <c>PaymentAuthorizedDomainEventHandler</c>. Untagged: the amount belongs
    /// on an order, the customer on a trace, and the currency is single-valued until multi-currency
    /// is built.
    /// </summary>
    public static void RecordAuthorized() => Authorized.Add(1);

    /// <summary>
    /// Money taken. Read against <see cref="RecordAuthorized"/> rather than alone — the gap between
    /// the two curves is orders sitting unaccepted, a restaurant-operations signal that happens to be
    /// visible from here.
    /// </summary>
    public static void RecordCaptured() => Captured.Add(1);

    /// <summary>
    /// An authorization that did not happen, by reason. This is what the failure-rate alert is built
    /// on, and the reason tag is what makes it actionable: a spike in <c>insufficient_funds</c> is
    /// the customers' problem, a spike in <c>gateway_error</c> is ours, and a spike in
    /// <c>no_payment_method</c> is Orders' <c>CanPayByCard</c> replica lagging (§6.3).
    /// </summary>
    public static void RecordFailed(string reason) =>
        Failed.Add(1, new KeyValuePair<string, object?>(ReasonTagName, reason));

    /// <summary>
    /// A hold given up uncharged. <b>Recorded from the command handler, not the domain-event
    /// handler</b> — the one business instrument here that departs from the module convention, and it
    /// does so because <c>PaymentReleasedDomainEvent</c> deliberately carries no reason (§9): the
    /// money does not care why the order ended, so the event does not say. The trigger is known only
    /// where the inbox handler that sent the command knew it, so the command carries it and the
    /// measurement is taken there — last, after <c>SaveChangesAsync</c>, and only on the path that
    /// actually released. A redelivered <c>OrderRejected</c> finds the payment already
    /// <c>Released</c> and returns long before reaching it.
    /// </summary>
    public static void RecordReleased(string trigger) =>
        Released.Add(1, new KeyValuePair<string, object?>(TriggerTagName, trigger));

    /// <summary>
    /// A refund the provider accepted. Deliberately not paired with a <c>refunds.failed</c> counter:
    /// three of that outcome's four reasons need a person inside the business rather than an operator
    /// looking at a graph, and Support's <c>support.refunds.decided</c> already carries the decision
    /// volume it would be read against.
    /// </summary>
    public static void RecordRefundSettled() => RefundsSettled.Add(1);

    /// <summary>
    /// One call to Stripe, from <c>StripePaymentGateway.InvokeAsync</c> — the single seam every
    /// provider call passes through, which is why the measurement cannot be forgotten on a method
    /// added later. Infrastructure rather than a domain event, because there is no state change to
    /// hang it on: a call that timed out changed nothing, and that is exactly the call this exists to
    /// show.
    /// <para>
    /// Tagged by outcome as well as operation because the three outcomes have different
    /// distributions — a refusal comes back in one normal round trip, a transient fault comes back
    /// after the SDK's own retries — and mixing them makes the p95 describe neither.
    /// </para>
    /// </summary>
    public static void RecordGatewayCall(string operation, string outcome, double durationInSeconds) =>
        GatewayDuration.Record(
            durationInSeconds,
            new KeyValuePair<string, object?>(OperationTagName, operation),
            new KeyValuePair<string, object?>(OutcomeTagName, outcome));

    /// <summary>
    /// How long a verified provider event waited between being recorded by the webhook endpoint and
    /// being acted on by the outbox — a seventh instrument §11.1's table does not list, added because
    /// the milestone also asks for a webhook-processing-lag alert and nothing else in the platform
    /// measures that gap.
    /// <para>
    /// It is the reconciling path's health, and the reconciling path is what stands in for the retry
    /// neither outbox nor inbox job performs (§5.6). A lag that grows means the confirmations Stripe
    /// sends are queueing behind something, which on this service means an authorization a customer
    /// is waiting on.
    /// </para>
    /// </summary>
    public static void RecordWebhookLag(string eventType, double lagInSeconds) =>
        WebhookLag.Record(lagInSeconds, new KeyValuePair<string, object?>(EventTypeTagName, eventType));
}
