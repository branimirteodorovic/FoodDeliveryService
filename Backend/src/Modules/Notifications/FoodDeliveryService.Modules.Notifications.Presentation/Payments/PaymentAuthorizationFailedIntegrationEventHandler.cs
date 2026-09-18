using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Application.Notifications.SendPaymentFailed;
using FoodDeliveryService.Modules.Payments.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Notifications.Presentation.Payments;

/// <summary>
/// Emails the customer that their card was declined and the order did not go through — Feature 3.8
/// Milestone H, §10.4 (dispatched by ProcessInboxJob, idempotent via the inbox).
/// <para>
/// This service consumes Payments' failure event rather than Orders' cancellation on purpose: the
/// order is cancelled either way, and only this event knows whether it was cancelled because a card
/// was refused or because the customer changed their mind. Those are not the same email.
/// </para>
/// </summary>
internal sealed class PaymentAuthorizationFailedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<PaymentAuthorizationFailedIntegrationEvent>
{
    public override async Task Handle(
        PaymentAuthorizationFailedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Result result = await sender.Send(
            new SendPaymentFailedCommand(
                integrationEvent.CustomerId,
                integrationEvent.OrderId,
                integrationEvent.Reason),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(SendPaymentFailedCommand),
                result.Error);
        }
    }
}
