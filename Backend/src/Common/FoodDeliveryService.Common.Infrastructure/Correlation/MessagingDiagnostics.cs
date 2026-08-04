using System.Diagnostics;

namespace FoodDeliveryService.Common.Infrastructure.Correlation;

/// <summary>
/// The <see cref="ActivitySource"/> the outbox/inbox dispatch spans are recorded on. A plain source
/// rather than a <c>{Module}Diagnostics</c> holder over <c>AppDiagnostics</c>: the dispatch loop is
/// infrastructure shared by every module, it emits no business metrics, and it must be registered
/// once by <c>AddInfrastructure</c> rather than eleven times by eleven hosts.
/// </summary>
public static class MessagingDiagnostics
{
    /// <summary>
    /// Registered with <c>AddSource</c> in <c>AddInfrastructure</c>. An unregistered source never
    /// errors — <c>StartActivity</c> simply returns null and the spans silently do not exist.
    /// </summary>
    public const string Name = "FoodDeliveryService.Messaging";

    /// <summary>The span name prefix for <c>ProcessOutboxJob</c>'s per-message dispatch.</summary>
    public const string OutboxDispatch = "outbox.dispatch";

    /// <summary>The span name prefix for <c>ProcessInboxJob</c>'s per-message dispatch.</summary>
    public const string InboxDispatch = "inbox.dispatch";

    public static readonly ActivitySource ActivitySource = new(Name);
}
