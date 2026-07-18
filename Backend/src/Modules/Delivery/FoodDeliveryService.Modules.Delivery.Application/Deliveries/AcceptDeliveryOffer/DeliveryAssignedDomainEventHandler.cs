using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;
using FoodDeliveryService.Modules.Delivery.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.AcceptDeliveryOffer;

// Publishes the full-snapshot DriverAssigned event — including the driver's name and vehicle — so
// Notifications can send "your driver is Alex" without calling back (hard rule #9).
internal sealed class DeliveryAssignedDomainEventHandler(ISender sender, IEventBus eventBus)
    : DomainEventHandler<DeliveryAssignedDomainEvent>
{
    public override async Task Handle(
        DeliveryAssignedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        Result<DriverAssignmentDetailsResponse> result = await sender.Send(
            new GetDriverAssignmentDetailsQuery(domainEvent.DriverId),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(GetDriverAssignmentDetailsQuery),
                result.Error);
        }

        await eventBus.PublishAsync(
            new DriverAssignedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.OrderId,
                domainEvent.DeliveryId,
                domainEvent.DriverId,
                result.Value.FirstName,
                result.Value.LastName,
                result.Value.VehicleType.ToString(),
                domainEvent.AssignedOnUtc),
            cancellationToken);
    }
}
