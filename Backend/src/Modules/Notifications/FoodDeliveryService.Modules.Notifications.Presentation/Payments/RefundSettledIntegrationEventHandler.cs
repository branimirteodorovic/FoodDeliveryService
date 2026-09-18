using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Application.Notifications.SendRefundSettled;
using FoodDeliveryService.Modules.Payments.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Notifications.Presentation.Payments;

/// <summary>
/// Emails the customer that their refund has actually been sent — Feature 3.8 Milestone H, §10.4
/// (dispatched by ProcessInboxJob, idempotent via the inbox).
/// <para>
/// The pair to <c>RefundApprovedIntegrationEventHandler</c>, which says the decision was made. Both
/// are sent, because they are answers to different questions and the gap between them is real: an
/// approval that Payments then cannot pay never reaches this handler at all.
/// </para>
/// <para>
/// <b><c>RefundFailedIntegrationEvent</c> is deliberately not consumed here.</b> Its reasons are
/// things a person inside the business has to act on — a cash order to settle by hand, a capture to
/// look into, an amount to re-raise — and none of them are things the customer can do. The agent
/// working the ticket learns about it first, which is the right order.
/// </para>
/// </summary>
internal sealed class RefundSettledIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<RefundSettledIntegrationEvent>
{
    public override async Task Handle(
        RefundSettledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Result result = await sender.Send(
            new SendRefundSettledCommand(
                integrationEvent.CustomerId,
                integrationEvent.TicketReference,
                integrationEvent.Amount),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(SendRefundSettledCommand),
                result.Error);
        }
    }
}
