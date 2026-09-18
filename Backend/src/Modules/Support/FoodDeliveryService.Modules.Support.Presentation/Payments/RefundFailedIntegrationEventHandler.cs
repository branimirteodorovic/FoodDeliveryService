using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.IntegrationEvents;
using FoodDeliveryService.Modules.Support.Application.Refunds.SettleRefund;
using MediatR;

namespace FoodDeliveryService.Modules.Support.Presentation.Payments;

/// <summary>
/// An approved refund moved no money, and the queue has to show it — Feature 3.8 Milestone H,
/// §10.2.
/// <para>
/// This is the handler that keeps the reversal honest in the direction that matters. Making refunds
/// real is easy to do halfway: publish the approval, let Payments try, and leave the request saying
/// "approved" when it does not work. Then an agent has told a customer their money is coming back
/// and nothing anywhere disagrees. The reason lands on the request and in the ticket's audit log,
/// which are the two places an agent looks.
/// </para>
/// </summary>
internal sealed class RefundFailedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<RefundFailedIntegrationEvent>
{
    public override async Task Handle(
        RefundFailedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Result result = await sender.Send(
            new SettleRefundCommand(
                integrationEvent.RefundRequestId,
                Settled: false,
                integrationEvent.Reason),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(SettleRefundCommand),
                result.Error);
        }
    }
}
