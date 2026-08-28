using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.AssignTicket;

/// <summary>
/// Assignment by name. An agent may target only themselves — a claim through a different door, and
/// one that unlike <c>ClaimTicketCommand</c> may take a ticket that is already in progress. Naming
/// anybody else requires the administrator bypass, checked in the handler.
/// </summary>
public sealed record AssignTicketCommand(Guid TicketId, Guid AgentId, string? Reason) : ICommand;
