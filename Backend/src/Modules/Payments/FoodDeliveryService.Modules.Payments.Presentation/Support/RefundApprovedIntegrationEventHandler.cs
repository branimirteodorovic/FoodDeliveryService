using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Refunds.RefundPayment;
using FoodDeliveryService.Modules.Support.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Payments.Presentation.Support;

/// <summary>
/// An administrator agreed to a refund, so the money goes back — Feature 3.8 Milestone H, §1.3
/// step 7. The first thing in the platform ever to act on a Support event.
/// <para>
/// <b><c>RefundRejectedIntegrationEvent</c> is deliberately not consumed.</b> A rejected request
/// means no money moves, which is already what happens; a handler for it would have nothing to do
/// but succeed.
/// </para>
/// <para>
/// The approval is the authorization for everything below it. This service performs no check of its
/// own on who agreed — segregation of duties is enforced inside Support's aggregate, and the event
/// carries both actors so that it stays checkable from outside — it only enforces the ceiling
/// against what was actually captured (§10.1).
/// </para>
/// </summary>
internal sealed class RefundApprovedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<RefundApprovedIntegrationEvent>
{
    public override async Task Handle(
        RefundApprovedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Result result = await sender.Send(
            new RefundPaymentCommand(
                integrationEvent.RefundRequestId,
                integrationEvent.TicketId,
                integrationEvent.TicketReference,
                integrationEvent.OrderId,
                integrationEvent.CustomerId,
                integrationEvent.Amount),
            cancellationToken);

        if (result.IsFailure)
        {
            // Only reached when the refund could not even be recorded — a lost lock, a currency the
            // platform cannot build. A refund that was refused for a business reason returns success
            // from there, because it has already been published as RefundFailed. The inbox records
            // this and marks the message processed; it does not retry (§5.6).
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(RefundPaymentCommand),
                result.Error);
        }
    }
}
