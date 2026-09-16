using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Orders.FailOrderPayment;
using FoodDeliveryService.Modules.Payments.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Orders.Presentation.Payments;

/// <summary>
/// The card was refused, so the order ends — Feature 3.8 Milestone F, §8.3.
/// <para>
/// The reason travels through to the aggregate and onto its domain event rather than being flattened
/// to "cancelled": Orders has no view of a card, so a description it invented here would be a guess,
/// and the bounded code Payments published is the only account of the refusal anyone should repeat.
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
            new FailOrderPaymentCommand(
                integrationEvent.OrderId,
                integrationEvent.Reason,
                integrationEvent.FailedOnUtc),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(FailOrderPaymentCommand),
                result.Error);
        }
    }
}
