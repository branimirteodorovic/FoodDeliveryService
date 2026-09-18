using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Application.Notifications.SendRefundDecision;
using FoodDeliveryService.Modules.Support.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Notifications.Presentation.Support;

/// <summary>
/// Emails the customer that their refund request was approved (dispatched by ProcessInboxJob,
/// idempotent via the inbox — a duplicate delivery never produces a second email).
/// <para>
/// The email says the decision was made and that the refund is being processed — never that the
/// money has arrived. Feature 3.8 put a real refund behind the approval, but it runs asynchronously
/// and can still fail (a cash order, an uncaptured payment), so the message that says the money has
/// actually been sent is the separate one driven by <c>RefundSettledIntegrationEvent</c>. Getting
/// that wording right matters more here than anywhere else in the module, because this is the one
/// place the record reaches a customer.
/// </para>
/// </summary>
internal sealed class RefundApprovedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<RefundApprovedIntegrationEvent>
{
    public override async Task Handle(
        RefundApprovedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new SendRefundDecisionCommand(
                integrationEvent.CustomerId,
                integrationEvent.TicketReference,
                integrationEvent.Amount,
                Approved: true,
                integrationEvent.DecisionNote),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(SendRefundDecisionCommand),
                result.Error);
        }
    }
}
