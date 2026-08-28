namespace FoodDeliveryService.Modules.Support.Domain.Tickets;

public enum TicketStatus
{
    /// <summary>Opened and sitting in the queue — nobody has picked it up yet.</summary>
    Open = 0,

    /// <summary>An agent is assigned and working it.</summary>
    InProgress = 1,

    /// <summary>The agent believes it is done. Still reopenable for 7 days.</summary>
    Resolved = 2,

    /// <summary>Handed up to a supervisor. Keeps its current assignee.</summary>
    Escalated = 3,

    /// <summary>Terminal — nothing transitions out of Closed.</summary>
    Closed = 4
}
