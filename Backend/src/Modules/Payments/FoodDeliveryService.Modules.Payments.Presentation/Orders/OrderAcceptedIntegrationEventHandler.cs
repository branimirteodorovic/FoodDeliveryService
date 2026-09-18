using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using FoodDeliveryService.Modules.Payments.Application.Payments.CapturePayment;
using MediatR;

namespace FoodDeliveryService.Modules.Payments.Presentation.Orders;

/// <summary>
/// The restaurant took the order on, so the hold becomes a charge — Feature 3.8 Milestone G, §1.3
/// step 5.
/// <para>
/// <b>Cash orders are not filtered here, unlike the placement handler.</b> This event carries no
/// payment method, and Milestone F's fix for that — putting one on the contract — is not worth
/// repeating on three more events when absence already answers the question: a cash order has no
/// <c>Payment</c> row, and the handler treats that as nothing to do. §9.1.
/// </para>
/// </summary>
internal sealed class OrderAcceptedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<OrderAcceptedIntegrationEvent>
{
    public override async Task Handle(
        OrderAcceptedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Result result = await sender.Send(
            new CapturePaymentCommand(integrationEvent.OrderId),
            cancellationToken);

        if (result.IsFailure)
        {
            // The inbox records this and marks the message processed — it does not retry (§5.6).
            // Throwing is still how a capture that did not happen becomes visible: unlike a decline,
            // there is no event published about it and no other record that the money was not taken.
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(CapturePaymentCommand),
                result.Error);
        }
    }
}
