using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.IntegrationEvents;
using FoodDeliveryService.Modules.Support.Application.Refunds.SettleRefund;
using MediatR;

namespace FoodDeliveryService.Modules.Support.Presentation.Payments;

/// <summary>
/// The money went back, so the request that asked for it says so — Feature 3.8 Milestone H, §10.2.
/// The first thing this service consumes from Payments, and the answer to the approval it published
/// itself.
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
            new SettleRefundCommand(
                integrationEvent.RefundRequestId,
                Settled: true,
                FailureReason: null),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(SettleRefundCommand),
                result.Error);
        }
    }
}
