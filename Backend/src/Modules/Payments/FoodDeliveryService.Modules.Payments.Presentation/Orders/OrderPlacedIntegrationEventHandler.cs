using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using FoodDeliveryService.Modules.Payments.Application.Payments.AuthorizePayment;
using MediatR;

namespace FoodDeliveryService.Modules.Payments.Presentation.Orders;

/// <summary>
/// An order was placed — authorize it if there is anything to authorize. Feature 3.8 Milestone F,
/// §1.3 step 3.
/// <para>
/// <b>Cash orders are skipped entirely and leave no trace here.</b> No <c>Payment</c> row is
/// created for them (§8.1): there is nothing to hold, and a row would put every cash order into a
/// state machine with no way out of it. The order's own <c>PaymentStatus.NotRequired</c> is the
/// whole record of that decision, and it lives in Orders because that is the module that knows the
/// order exists.
/// </para>
/// <para>
/// This is also the milestone where the flow stops being a request and becomes a saga leg: the
/// charge is off-session, against a card the customer saved earlier, driven by an event that was
/// already being published. Nothing synchronous crosses a service boundary (hard rule #4).
/// </para>
/// </summary>
internal sealed class OrderPlacedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    public override async Task Handle(
        OrderPlacedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!string.Equals(integrationEvent.PaymentMethod, OrderPaymentMethods.Card, StringComparison.Ordinal))
        {
            return;
        }

        Result result = await sender.Send(
            new AuthorizePaymentCommand(
                integrationEvent.OrderId,
                integrationEvent.CustomerId,
                integrationEvent.Subtotal),
            cancellationToken);

        if (result.IsFailure)
        {
            // The inbox job records this on the message row and marks it processed — it does not
            // retry (§5.6). Throwing is still right: it is what distinguishes "this payment was not
            // handled" from "this payment was declined", which is a success here and is published as
            // its own event. The reconciling webhook (§7) is what finishes the former.
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(AuthorizePaymentCommand),
                result.Error);
        }
    }
}
