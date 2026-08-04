using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Infrastructure.Correlation;
using FoodDeliveryService.Common.Infrastructure.Serialization;
using FoodDeliveryService.Common.Presentation.Correlation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Newtonsoft.Json;

namespace FoodDeliveryService.Common.Infrastructure.Outbox;

/// <summary>
/// EF Core <see cref="SaveChangesInterceptor"/> implementing the transactional-outbox write side:
/// just before SaveChanges, it drains the domain events raised by tracked entities and stores
/// them as JSON rows in outbox_messages — in the SAME transaction as the business change, so an
/// event can never be persisted without its state change (or vice versa). The Quartz-scheduled
/// ProcessOutboxJob later reads these rows and dispatches the domain event handlers, which
/// publish integration events to RabbitMQ. Registered on every module DbContext.
/// <para>
/// It is also where the correlation id and trace context of the causing request are copied ONTO the
/// row — the first of the two database handoffs where correlation would otherwise be lost, because
/// the request is finished long before the job that dispatches the event runs.
/// </para>
/// </summary>
public sealed class InsertOutboxMessagesInterceptor(CorrelationContext correlationContext) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            InsertOutboxMessages(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void InsertOutboxMessages(DbContext context)
    {
        // Read once per SaveChanges rather than per event: every event in this transaction was
        // raised by the same unit of work, so they share one correlation id by construction.
        string? correlationId = MessageCorrelationColumns.FitCorrelationId(correlationContext.CorrelationId);

        string? traceParent = MessageCorrelationColumns.FitTraceParent(correlationContext.TraceParent);

        var outboxMessages = context
            .ChangeTracker
            .Entries<Entity>()
            .Select(entry => entry.Entity)
            .SelectMany(entity =>
            {
                IReadOnlyCollection<IDomainEvent> domainEvents = entity.DomainEvents;

                entity.ClearDomainEvents();

                return domainEvents;
            })
            .Select(domainEvent => new OutboxMessage
            {
                Id = domainEvent.Id,
                Type = domainEvent.GetType().Name,
                Content = JsonConvert.SerializeObject(domainEvent, SerializerSettings.Instance),
                OccurredOnUtc = domainEvent.OccurredOnUtc,
                CorrelationId = correlationId,
                TraceParent = traceParent
            })
            .ToList();

        context.Set<OutboxMessage>().AddRange(outboxMessages);
    }
}
