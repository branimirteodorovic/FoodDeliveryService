using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Orders.MarkPaymentReleased;
using FoodDeliveryService.Modules.Payments.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Orders.Presentation.Payments;

/// <summary>
/// The hold is gone and the customer was never charged — Feature 3.8 Milestone G, §1.3 step 6.
/// <para>
/// The order is already <c>Rejected</c> or <c>Cancelled</c> by the time this arrives — it is the
/// event Payments published <em>because</em> of that ending. All that is recorded here is that the
/// money followed, which is the difference between "your order was cancelled" and "your order was
/// cancelled and nothing was taken from your card".
/// </para>
/// </summary>
internal sealed class PaymentReleasedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<PaymentReleasedIntegrationEvent>
{
    public override async Task Handle(
        PaymentReleasedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Result result = await sender.Send(
            new MarkPaymentReleasedCommand(integrationEvent.OrderId),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(MarkPaymentReleasedCommand),
                result.Error);
        }
    }
}
