namespace FoodDeliveryService.Modules.Support.Domain.Tickets;

/// <summary>
/// Who wrote a message. Not derived from the ticket (the author may be the customer, the assigned
/// agent, or a different agent entirely), and never taken from the request body — the application
/// layer decides it from the caller's permissions, because it is what the aggregate's
/// customers-may-not-write-internal-notes rule turns on.
/// </summary>
public enum TicketAuthorKind
{
    Customer = 0,

    /// <summary>A support agent or an administrator — anybody holding support-tickets:manage.</summary>
    Agent = 1,

    /// <summary>
    /// The platform itself. Reserved: nothing writes one yet, and it exists so an automated entry
    /// ("the refund was approved") is distinguishable from an agent who typed the same words.
    /// </summary>
    System = 2
}
