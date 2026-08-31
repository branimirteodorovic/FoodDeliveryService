using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Support.Domain.Tickets;
using FoodDeliveryService.Modules.Support.IntegrationEvents;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.PostTicketMessage;

/// <summary>
/// The module boundary for the ticket thread. Two things happen here and nowhere else: the decision
/// about which messages leave Support at all, and the truncation of the ones that do.
/// </summary>
internal sealed class TicketMessagePostedDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<TicketMessagePostedDomainEvent>
{
    /// <summary>
    /// How much of a reply travels with the event. Long enough that the notification says something
    /// ("we've issued your refund, it will appear in 3–5 days") and short enough that the whole
    /// conversation does not end up copied into every downstream store.
    /// </summary>
    private const int PreviewMaxLength = 300;

    private const string Ellipsis = "…";

    public override async Task Handle(
        TicketMessagePostedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        // The filter that matters, and the reason it is here rather than in each consumer: an
        // internal note put on the bus has already left the boundary that was supposed to contain
        // it, and no amount of care downstream can put it back. A customer's own message is dropped
        // for a duller reason — nobody needs an email about something they just typed.
        if (domainEvent.Visibility != TicketMessageVisibility.CustomerVisible ||
            domainEvent.AuthorKind != TicketAuthorKind.Agent)
        {
            return;
        }

        await eventBus.PublishAsync(
            new TicketMessagePostedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.TicketId,
                domainEvent.Reference,
                domainEvent.MessageId,
                domainEvent.CustomerId,
                domainEvent.AuthorId,
                domainEvent.Subject,
                Preview(domainEvent.Body),
                domainEvent.PostedOnUtc),
            cancellationToken);
    }

    private static string Preview(string body) =>
        body.Length <= PreviewMaxLength ? body : body[..PreviewMaxLength] + Ellipsis;
}
