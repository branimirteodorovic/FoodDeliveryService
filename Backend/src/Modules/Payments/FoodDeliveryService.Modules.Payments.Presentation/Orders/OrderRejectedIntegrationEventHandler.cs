using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using FoodDeliveryService.Modules.Payments.Application.Payments.ReleasePayment;
using MediatR;

namespace FoodDeliveryService.Modules.Payments.Presentation.Orders;

/// <summary>
/// The restaurant refused the order, so the hold is given up uncharged — Feature 3.8 Milestone G,
/// §1.3 step 6.
/// <para>
/// The rejection reason on the event is deliberately not carried into Payments: the money does not
/// care why the order ended, and a copy of that text here would be a second account of it living in
/// a service that has no business telling the story.
/// </para>
/// </summary>
internal sealed class OrderRejectedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<OrderRejectedIntegrationEvent>
{
    public override async Task Handle(
        OrderRejectedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Result result = await sender.Send(
            new ReleasePaymentCommand(integrationEvent.OrderId),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(ReleasePaymentCommand),
                result.Error);
        }
    }
}
