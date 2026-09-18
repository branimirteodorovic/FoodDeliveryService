using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Orders.MarkPaymentCaptured;
using FoodDeliveryService.Modules.Payments.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Orders.Presentation.Payments;

/// <summary>
/// The money has been taken — Feature 3.8 Milestone G, §1.3 step 5.
/// <para>
/// Nothing in the order's lifecycle turns on it: the restaurant accepted before Payments captured,
/// which is what put the order where it already is. The projection exists so that "was this
/// charged?" is answerable from the order, by a customer's order screen, by a support agent and by
/// §10's refund, without a synchronous call to Payments.
/// </para>
/// <para>
/// As with the authorization, the amount on the event is deliberately not copied: Orders has its own
/// <c>Subtotal</c>, and two services owning one number is two numbers that can disagree.
/// </para>
/// </summary>
internal sealed class PaymentCapturedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<PaymentCapturedIntegrationEvent>
{
    public override async Task Handle(
        PaymentCapturedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Result result = await sender.Send(
            new MarkPaymentCapturedCommand(integrationEvent.OrderId),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(MarkPaymentCapturedCommand),
                result.Error);
        }
    }
}
