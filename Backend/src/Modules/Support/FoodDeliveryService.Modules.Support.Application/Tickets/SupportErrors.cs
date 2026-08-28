using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Application.Tickets;

/// <summary>
/// Authorization failures that belong to the application layer rather than to the aggregate — the
/// aggregate has no notion of who is calling, only of what state it is in. Everything about the
/// ticket itself lives in <c>TicketErrors</c>.
/// </summary>
internal static class SupportErrors
{
    internal static readonly Error NotAuthorizedToActOnBehalfOfCustomer = Error.Problem(
        "Support.NotAuthorizedToActOnBehalfOfCustomer",
        "Only a support agent may open a ticket on behalf of another customer");

    // Agents claim their own work; routing somebody else's is the administrator bypass. An agent
    // who tries it is refused rather than silently reinterpreted as claiming it themselves.
    internal static readonly Error NotAuthorizedToAssignAnotherAgent = Error.Problem(
        "Support.NotAuthorizedToAssignAnotherAgent",
        "Only an administrator may assign a ticket to another agent");

    // Checked against the local agent replica, not against Users — a NotFound here means the id
    // names nobody this module knows to be a support agent or administrator.
    internal static Error AgentNotFound(Guid agentId) => Error.NotFound(
        "Support.AgentNotFound",
        $"No active support agent with the identifier {agentId} was found");
}
