using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Application.Notifications.SendSupportTicketReply;
using FoodDeliveryService.Modules.Support.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Notifications.Presentation.Support;

/// <summary>
/// Emails the customer when a support agent replies on their ticket (dispatched by ProcessInboxJob,
/// idempotent via the inbox — a duplicate delivery never produces a second email).
/// <para>
/// There is no visibility check here, and that is deliberate rather than an omission: Support only
/// publishes this event for customer-visible agent messages, so an internal note never reaches this
/// module. Re-deriving the rule on this side would create a second copy of it that could drift from
/// the one that matters, and it would imply notes are expected to arrive here — which is exactly the
/// assumption the publishing filter exists to make false.
/// </para>
/// </summary>
internal sealed class TicketMessagePostedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<TicketMessagePostedIntegrationEvent>
{
    public override async Task Handle(
        TicketMessagePostedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new SendSupportTicketReplyCommand(
                integrationEvent.CustomerId,
                integrationEvent.Reference,
                integrationEvent.Subject,
                integrationEvent.Preview),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(SendSupportTicketReplyCommand),
                result.Error);
        }
    }
}
