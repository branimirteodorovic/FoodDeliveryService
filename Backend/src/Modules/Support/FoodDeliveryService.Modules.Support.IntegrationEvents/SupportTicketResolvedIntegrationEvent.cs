using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Support.IntegrationEvents;

/// <summary>
/// An agent resolved a ticket. Carries <see cref="OpenedOnUtc"/> alongside
/// <see cref="ResolvedOnUtc"/> so a consumer can compute the resolution time without querying
/// Support (hard rule #9 and hard rule #5 in the same field).
/// </summary>
public sealed class SupportTicketResolvedIntegrationEvent : IntegrationEvent
{
    public SupportTicketResolvedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid ticketId,
        string reference,
        Guid customerId,
        Guid? orderId,
        Guid agentId,
        string category,
        string resolution,
        DateTime openedOnUtc,
        DateTime resolvedOnUtc)
        : base(id, occurredOnUtc)
    {
        TicketId = ticketId;
        Reference = reference;
        CustomerId = customerId;
        OrderId = orderId;
        AgentId = agentId;
        Category = category;
        Resolution = resolution;
        OpenedOnUtc = openedOnUtc;
        ResolvedOnUtc = resolvedOnUtc;
    }

    public Guid TicketId { get; init; }

    public string Reference { get; init; }

    public Guid CustomerId { get; init; }

    public Guid? OrderId { get; init; }

    public Guid AgentId { get; init; }

    public string Category { get; init; }

    public string Resolution { get; init; }

    public DateTime OpenedOnUtc { get; init; }

    public DateTime ResolvedOnUtc { get; init; }
}
