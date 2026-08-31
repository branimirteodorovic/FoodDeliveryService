namespace FoodDeliveryService.Modules.Support.Domain.Tickets;

/// <summary>
/// Who may read a message. The two values are a security boundary, not a display preference: an
/// <see cref="InternalNote"/> is filtered out in the SQL of the customer-facing read rather than in
/// a DTO mapper, so no projection change can leak one.
/// </summary>
public enum TicketMessageVisibility
{
    /// <summary>Part of the conversation the customer sees, and the only kind they may write.</summary>
    CustomerVisible = 0,

    /// <summary>Agent-to-agent. Never rendered to a customer and never leaves the module.</summary>
    InternalNote = 1
}
