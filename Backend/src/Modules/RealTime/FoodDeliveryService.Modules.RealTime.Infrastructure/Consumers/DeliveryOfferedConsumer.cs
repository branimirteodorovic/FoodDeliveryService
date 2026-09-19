using FoodDeliveryService.Modules.Delivery.IntegrationEvents;
using FoodDeliveryService.Modules.RealTime.Application.RealTime;
using MassTransit;

namespace FoodDeliveryService.Modules.RealTime.Infrastructure.Consumers;

/// <summary>
/// Pushes a delivery offer to the driver it was made to. Same direct-consumer, own-queue,
/// no-inbox shape as the <see cref="DeliveryStatusConsumer{TEvent}"/> siblings (their justification
/// applies unchanged), but it is not one of them: those resolve the <em>customer</em> from the
/// routing map and send an <see cref="OrderStatusFrame"/> on the order timeline. This one has its
/// audience on the event already — <c>DriverId</c> — and an offer is not a timeline entry: the
/// customer has no interest in which driver is being asked, and until somebody accepts, nothing
/// about the order has changed.
/// <para>
/// Consequently there is no routing-map lookup and no fallible resolution step: if the driver is
/// not connected the send simply reaches an empty group, which is the correct outcome.
/// </para>
/// <para>
/// <b>No retraction frame is ever sent.</b> An offer ends by lapsing, by being declined, or by
/// being accepted, and only the last of those is something this service hears about. Re-offering
/// to another driver publishes a new <c>DeliveryOffered</c> to <em>that</em> driver and says
/// nothing to this one. The client therefore self-expires on <c>OfferExpiresOnUtc</c> — which it
/// can do correctly and locally, without a frame that might itself be dropped. Adding a retraction
/// would put a second, contradictory expiry mechanism behind a best-effort transport: a client that
/// missed it would hold a stale offer forever, so the deadline would have to stay anyway.
/// </para>
/// </summary>
internal sealed class DeliveryOfferedConsumer(IRealTimeNotifier notifier)
    : IConsumer<DeliveryOfferedIntegrationEvent>
{
    public Task Consume(ConsumeContext<DeliveryOfferedIntegrationEvent> context)
    {
        DeliveryOfferedIntegrationEvent message = context.Message;

        // The driver's own group: Driver.Id is the Users service's user id, which is the hub's sub.
        return notifier.NotifyDriverAsync(
            message.DriverId,
            new DeliveryOfferFrame(message.DeliveryId, message.OrderId, message.OfferExpiresOnUtc),
            context.CancellationToken);
    }
}
