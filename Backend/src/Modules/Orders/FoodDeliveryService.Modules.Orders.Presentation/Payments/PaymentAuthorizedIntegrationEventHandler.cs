using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Orders.MarkPaymentAuthorized;
using FoodDeliveryService.Modules.Payments.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Orders.Presentation.Payments;

/// <summary>
/// The hold is on the card, so the restaurant may now accept — Feature 3.8 Milestone F.
/// <para>
/// The amount and the currency on the event are deliberately not copied anywhere: Orders already has
/// its own <c>Subtotal</c> snapshot, and a second copy of the same number owned by a different
/// service is two numbers that can disagree. What Orders takes from this event is the fact that it
/// happened.
/// </para>
/// </summary>
internal sealed class PaymentAuthorizedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<PaymentAuthorizedIntegrationEvent>
{
    public override async Task Handle(
        PaymentAuthorizedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Result result = await sender.Send(
            new MarkPaymentAuthorizedCommand(integrationEvent.OrderId),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(MarkPaymentAuthorizedCommand),
                result.Error);
        }
    }
}
