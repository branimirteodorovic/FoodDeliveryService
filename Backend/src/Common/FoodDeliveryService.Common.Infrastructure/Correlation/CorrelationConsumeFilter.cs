using FoodDeliveryService.Common.Presentation.Correlation;
using MassTransit;

namespace FoodDeliveryService.Common.Infrastructure.Correlation;

/// <summary>
/// Reads the correlation id <see cref="CorrelationPublishFilter{TMessage}"/> put on the message back
/// into the ambient <see cref="CorrelationContext"/>, so <c>IntegrationEventConsumer</c> stamps it
/// onto the <c>inbox_messages</c> row it writes.
/// <para>
/// A direct consumer — one that skips the inbox, as Real-Time's status consumers do — gets the id in
/// the ambient context but not on its log lines: nothing opens a <c>MessageDispatchScope</c> for it,
/// because there is no row to open one from. Its trace is intact (MassTransit propagates
/// <c>traceparent</c>), so it is reachable from Jaeger; enriching those log lines would be a small
/// separate change to whatever opens that consumer's scope.
/// </para>
/// </summary>
internal sealed class CorrelationConsumeFilter<TMessage>(CorrelationContext correlationContext)
    : IFilter<ConsumeContext<TMessage>>
    where TMessage : class
{
    public async Task Send(ConsumeContext<TMessage> context, IPipe<ConsumeContext<TMessage>> next)
    {
        // Pushed even when the header is absent — a message from a producer that predates this
        // filter. The context then reports the ambient trace id instead, which is still the consume
        // span MassTransit put inside the producing trace.
        string? correlationId = context.Headers.Get<string>(CorrelationHeaders.CorrelationId);

        using (correlationContext.Push(correlationId, traceParent: null))
        {
            await next.Send(context);
        }
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("correlationId");
}
