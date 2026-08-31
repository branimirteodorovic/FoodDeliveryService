using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Support.IntegrationEvents;

/// <summary>
/// An agent replied to a customer on a support ticket.
/// <para>
/// Published for <b>customer-visible agent messages only</b>. A customer does not get emailed about
/// their own message, and an internal note must never leave this service at all — that filter lives
/// in the domain-event handler, before the publish, rather than in each consumer, because a note
/// that reaches the bus is already outside the boundary that was supposed to contain it.
/// </para>
/// <para>
/// <see cref="Preview"/> is a truncation, not the message. The full body stays in Support: this
/// event exists to tell a consumer that a reply happened and to give an email or a push enough text
/// to be useful, and a support conversation copied in full into every downstream inbox is a
/// disclosure surface nothing here needs.
/// </para>
/// </summary>
public sealed class TicketMessagePostedIntegrationEvent : IntegrationEvent
{
    public TicketMessagePostedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid ticketId,
        string reference,
        Guid messageId,
        Guid customerId,
        Guid agentId,
        string subject,
        string preview,
        DateTime postedOnUtc)
        : base(id, occurredOnUtc)
    {
        TicketId = ticketId;
        Reference = reference;
        MessageId = messageId;
        CustomerId = customerId;
        AgentId = agentId;
        Subject = subject;
        Preview = preview;
        PostedOnUtc = postedOnUtc;
    }

    public Guid TicketId { get; init; }

    /// <summary>The SUP-00001234 the customer can quote back.</summary>
    public string Reference { get; init; }

    public Guid MessageId { get; init; }

    /// <summary>The recipient: the ticket's owner, not the author.</summary>
    public Guid CustomerId { get; init; }

    /// <summary>The agent who wrote the reply.</summary>
    public Guid AgentId { get; init; }

    public string Subject { get; init; }

    /// <summary>The first few hundred characters of the reply, ellipsized when cut.</summary>
    public string Preview { get; init; }

    public DateTime PostedOnUtc { get; init; }
}
