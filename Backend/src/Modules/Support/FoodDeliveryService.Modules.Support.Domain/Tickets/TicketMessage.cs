using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Tickets;

/// <summary>
/// One message in a ticket's thread. A child of the <see cref="Ticket"/> aggregate rather than an
/// aggregate of its own: a message is meaningless without its ticket, and posting one is a state
/// change <em>on</em> the ticket — it can stamp <c>FirstRespondedOnUtc</c> and it can pull a
/// resolved ticket back into progress.
/// <para>
/// Created only through <see cref="Ticket.PostMessage"/>, which owns every rule about who may write
/// what. Immutable once written: there is no edit path and no delete path, because a support thread
/// an agent can rewrite after the fact is not evidence of anything.
/// </para>
/// </summary>
public sealed class TicketMessage : Entity
{
    public const int BodyMaxLength = 4000;

    private TicketMessage()
    {
    }

    public Guid Id { get; private set; }

    public Guid TicketId { get; private set; }

    /// <summary>The customer or the agent who wrote it, from the authenticated caller.</summary>
    public Guid AuthorId { get; private set; }

    public TicketAuthorKind AuthorKind { get; private set; }

    public string Body { get; private set; }

    public TicketMessageVisibility Visibility { get; private set; }

    public DateTime PostedOnUtc { get; private set; }

    internal static TicketMessage Create(
        Guid ticketId,
        Guid authorId,
        TicketAuthorKind authorKind,
        string body,
        TicketMessageVisibility visibility,
        DateTime utcNow)
    {
        return new TicketMessage
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            AuthorId = authorId,
            AuthorKind = authorKind,
            Body = body,
            Visibility = visibility,
            PostedOnUtc = utcNow
        };
    }
}
