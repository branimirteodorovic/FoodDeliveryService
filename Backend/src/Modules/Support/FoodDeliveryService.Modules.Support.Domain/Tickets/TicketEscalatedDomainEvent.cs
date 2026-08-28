using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Tickets;

public sealed class TicketEscalatedDomainEvent(Guid ticketId, Guid agentId, string reason) : DomainEvent
{
    public Guid TicketId { get; init; } = ticketId;

    public Guid AgentId { get; init; } = agentId;

    public string Reason { get; init; } = reason;
}
