using FoodDeliveryService.Common.Presentation.Correlation;
using MassTransit;

namespace FoodDeliveryService.Common.Infrastructure.Correlation;

/// <summary>
/// Puts the ambient correlation id on every published message as a header, so the consuming service
/// can write it onto its <c>inbox_messages</c> row and carry it into its own handlers.
/// <para>
/// A <b>header</b> rather than MassTransit's envelope <c>CorrelationId</c>: that property is a
/// <see cref="Guid"/> and this id may be an inbound client string. The trace context needs nothing
/// here — the OpenTelemetry MassTransit instrumentation already carries <c>traceparent</c> across
/// the broker, which is why the broker hop was never the leg that lost correlation.
/// </para>
/// </summary>
internal sealed class CorrelationPublishFilter<TMessage>(CorrelationContext correlationContext)
    : IFilter<PublishContext<TMessage>>
    where TMessage : class
{
    public Task Send(PublishContext<TMessage> context, IPipe<PublishContext<TMessage>> next)
    {
        string? correlationId = correlationContext.CorrelationId;

        if (!string.IsNullOrEmpty(correlationId))
        {
            context.Headers.Set(CorrelationHeaders.CorrelationId, correlationId);
        }

        return next.Send(context);
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("correlationId");
}
