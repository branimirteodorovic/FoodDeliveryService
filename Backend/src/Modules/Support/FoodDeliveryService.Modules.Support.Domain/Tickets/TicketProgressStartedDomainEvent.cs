using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Tickets;

public sealed class TicketProgressStartedDomainEvent(Guid ticketId, Guid agentId) : DomainEvent
{
    public Guid TicketId { get; init; } = ticketId;

    public Guid AgentId { get; init; } = agentId;
}
