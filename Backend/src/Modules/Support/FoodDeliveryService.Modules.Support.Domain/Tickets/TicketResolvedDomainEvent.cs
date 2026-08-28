using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Tickets;

/// <summary>
/// Full snapshot again — SupportTicketResolvedIntegrationEvent is built straight from this, and
/// OpenedOnUtc travels with it so a consumer can compute the resolution time without asking.
/// </summary>
public sealed class TicketResolvedDomainEvent(
    Guid ticketId,
    string reference,
    Guid customerId,
    Guid? orderId,
    Guid agentId,
    TicketCategory category,
    string resolution,
    DateTime openedOnUtc,
    DateTime resolvedOnUtc) : DomainEvent
{
    public Guid TicketId { get; init; } = ticketId;

    public string Reference { get; init; } = reference;

    public Guid CustomerId { get; init; } = customerId;

    public Guid? OrderId { get; init; } = orderId;

    public Guid AgentId { get; init; } = agentId;

    public TicketCategory Category { get; init; } = category;

    public string Resolution { get; init; } = resolution;

    public DateTime OpenedOnUtc { get; init; } = openedOnUtc;

    public DateTime ResolvedOnUtc { get; init; } = resolvedOnUtc;
}
