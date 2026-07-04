using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Infrastructure.Serialization;
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
/// </summary>
public sealed class InsertOutboxMessagesInterceptor : SaveChangesInterceptor
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

    private static void InsertOutboxMessages(DbContext context)
    {
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
                OccurredOnUtc = domainEvent.OccurredOnUtc
            })
            .ToList();

        context.Set<OutboxMessage>().AddRange(outboxMessages);
    }
}
