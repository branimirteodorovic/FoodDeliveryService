using FoodDeliveryService.Common.Application.EventBus;
using MassTransit;

namespace FoodDeliveryService.Common.Infrastructure.EventBus;

/// <summary>
/// Thin adapter over MassTransit's <see cref="IBus"/>: publishing an integration event here
/// sends it to a RabbitMQ fanout exchange, from which every subscribed service's queue gets a
/// copy. This is the only publishing API application code should use — going through MassTransit
/// keeps serialization, topology and OpenTelemetry trace propagation consistent. Domain event
/// handlers call this from ProcessOutboxJob after the local transaction has committed.
/// </summary>
internal sealed class EventBus(IBus bus) : IEventBus
{
    public async Task PublishAsync<T>(T integrationEvent, CancellationToken cancellationToken = default)
        where T : IIntegrationEvent
    {
        await bus.Publish(integrationEvent, cancellationToken);
    }
}
