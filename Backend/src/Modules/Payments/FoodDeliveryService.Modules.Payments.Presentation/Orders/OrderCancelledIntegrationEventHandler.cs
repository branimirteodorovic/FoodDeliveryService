using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using FoodDeliveryService.Modules.Payments.Application.Payments.ReleasePayment;
using MediatR;

namespace FoodDeliveryService.Modules.Payments.Presentation.Orders;

/// <summary>
/// The customer backed out, so the hold is given up uncharged — Feature 3.8 Milestone G, §1.3 step 6.
/// The same command as a rejection: two endings for the order, one instruction for the money.
/// <para>
/// <b>An order cancelled because its payment failed never reaches here</b>, and that is by design
/// rather than by luck: <c>Order.FailPayment</c> raises its own domain event instead of reusing
/// <c>Cancel</c> (§8.3), so no <c>OrderCancelled</c> is published for it and this handler is never
/// asked to release a hold that was never placed. The aggregate would absorb it (§8.6) — the point
/// is not to depend on that.
/// </para>
/// </summary>
internal sealed class OrderCancelledIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    public override async Task Handle(
        OrderCancelledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Result result = await sender.Send(
            new ReleasePaymentCommand(integrationEvent.OrderId, PaymentReleaseTrigger.Cancelled),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(ReleasePaymentCommand),
                result.Error);
        }
    }
}
