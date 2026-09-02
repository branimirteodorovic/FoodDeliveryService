using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Application.Notifications.SendRefundDecision;
using FoodDeliveryService.Modules.Support.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Notifications.Presentation.Support;

/// <summary>
/// Emails the customer that their refund request was declined. A separate handler rather than one
/// that branches on an outcome field, mirroring the two contracts Support publishes: the mistake
/// this shape makes impossible is a boolean read the wrong way round, which here would tell a
/// customer the opposite of what was decided.
/// </summary>
internal sealed class RefundRejectedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<RefundRejectedIntegrationEvent>
{
    public override async Task Handle(
        RefundRejectedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new SendRefundDecisionCommand(
                integrationEvent.CustomerId,
                integrationEvent.TicketReference,
                integrationEvent.Amount,
                Approved: false,
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
