using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Support.IntegrationEvents;

/// <summary>
/// A customer (or an agent on their behalf) opened a support ticket. Full snapshot, hard rule #9:
/// everything a live agent dashboard row or a notification needs is here, so no consumer has to
/// call back into Support.
///
/// Nothing consumes it yet. The RealTime dashboard frame and the Notifications templates are later
/// milestones; publishing from the start means neither of them needs a change on this side.
/// </summary>
public sealed class SupportTicketOpenedIntegrationEvent : IntegrationEvent
{
    public SupportTicketOpenedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid ticketId,
        string reference,
        Guid customerId,
        Guid? orderId,
        string subject,
        string category,
        string priority,
        string status,
        string source,
        Guid? assignedAgentId,
        DateTime openedOnUtc)
        : base(id, occurredOnUtc)
    {
        TicketId = ticketId;
        Reference = reference;
        CustomerId = customerId;
        OrderId = orderId;
        Subject = subject;
        Category = category;
        Priority = priority;
        Status = status;
        Source = source;
        AssignedAgentId = assignedAgentId;
        OpenedOnUtc = openedOnUtc;
    }

    public Guid TicketId { get; init; }

    public string Reference { get; init; }

    public Guid CustomerId { get; init; }

    public Guid? OrderId { get; init; }

    public string Subject { get; init; }

    // The lifecycle enums cross the bus as their names, not their numbers: a consumer in another
    // service has no reference to Support.Domain, and a reordered enum must not silently change
    // what an already-published message meant.
    public string Category { get; init; }

    public string Priority { get; init; }

    public string Status { get; init; }

    public string Source { get; init; }

    public Guid? AssignedAgentId { get; init; }

    public DateTime OpenedOnUtc { get; init; }
}
