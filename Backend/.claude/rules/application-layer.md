---
name: application-layer-rules
description: Rules that apply when working in FoodDeliveryService Application layer projects
paths: ["**/Application/**/*.cs", "**/Modules/**/*.Application/**/*.cs"]
---

# Application Layer Rules

You are currently editing an **Application layer** file. Application projects contain CQRS handlers, commands, queries, validators, and domain event handlers. They orchestrate domain logic but contain none themselves.

## Commands
```csharp
// Command definition: sealed record, implements ICommand<TResponse>
public sealed record Create{Entity}Command(
    string Name,
    Guid CategoryId
) : ICommand<Guid>;          // use ICommand (void) if no ID to return
```

## Command Handlers
```csharp
internal sealed class Create{Entity}CommandHandler(         // internal sealed — always
    I{Entity}Repository repository,
    IUnitOfWork unitOfWork)                                 // module's own IUnitOfWork abstraction
    : ICommandHandler<Create{Entity}Command, Guid>          // NOT IRequestHandler directly
{
    public async Task<Result<Guid>> Handle(Create{Entity}Command request, CancellationToken cancellationToken)
    {
        // Fetch domain dependencies
        // Call domain factory or method — NO business logic here
        Result<{Entity}> result = {Entity}.Create(request.Name, ...);
        if (result.IsFailure) return Result.Failure<Guid>(result.Error);

        repository.Insert(result.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);   // ALWAYS async, NEVER SaveChanges()
        return result.Value.Id;
    }
}
```

## Query Handlers — DAPPER ONLY
```csharp
internal sealed class Get{Entity}QueryHandler(IDbConnectionFactory dbConnectionFactory)
    : IQueryHandler<Get{Entity}Query, {Entity}Response>
{
    public async Task<Result<{Entity}Response>> Handle(Get{Entity}Query request, CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        // Raw SQL with Dapper — NEVER EF Core DbSet for reads.
        // Tables are snake_case in the default (public) schema — no schema prefix.
        const string sql = $"""
            SELECT id AS {nameof({Entity}Response.Id)}, name AS {nameof({Entity}Response.Name)}
            FROM {table} WHERE id = @{nameof(request.{Entity}Id)}
            """;

        var result = await connection.QuerySingleOrDefaultAsync<{Entity}Response>(sql, request);
        return result ?? Result.Failure<{Entity}Response>({Entity}Errors.NotFound);
    }
}
```

**NEVER use `DbSet<T>` or `_context.{Entities}` in query handlers.** Use `IDbConnectionFactory` + Dapper. Each service can only query its OWN database — if you need another service's data, it must be replicated locally via integration events.

## Validators
```csharp
internal sealed class Create{Entity}CommandValidator : AbstractValidator<Create{Entity}Command>
{
    public Create{Entity}CommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.CategoryId).NotEqual(Guid.Empty);
    }
}
```

## Concurrency: locking a contended write

Before writing a handler, ask: **does this read a value, decide on it, then write — where a concurrent caller could change that value in between, and losing the race double-books something scarce?** (A driver, a seat, a stock item, a one-per-order record.) If yes, the handler needs `IDistributedLock`; if no, skip this section entirely — most handlers don't need it.

This matters more than usual here because **no aggregate has an optimistic concurrency token**: two concurrent `SaveChangesAsync` calls on the same row both succeed. The domain guard (`if (Status != Available) return Failure`) is a check-then-act like any other — it only holds if the two callers are serialized.

```csharp
// Acquire BEFORE loading the aggregate — the check-then-act starts at the read.
await using IAsyncDisposable? handle = await distributedLock.TryAcquireAsync(
    {Module}Locks.{Resource}(id), {Module}Locks.Ttl, cancellationToken);

if (handle is null)
    return Result.Failure({Entity}Errors.SomethingInProgress);  // not Result.Success()
```

- Build keys/TTL in one shared static per module (see `DeliveryLocks`) — never inline the string.
- Returning `Result.Success()` on a lost acquisition **strands** the entity unless a retry exists. `ProcessInboxJob` does not retry (it records the error and marks the message processed); a Quartz scan only re-drives rows still matching its `WHERE`.
- Keep the domain guard as well. The lock serializes the callers; the aggregate is still what decides.

Reference: `Delivery.Application/.../AcceptDeliveryOffer/AcceptDeliveryOfferCommandHandler.cs`.

## Domain Event Handlers (publish integration events)
```csharp
internal sealed class {Entity}CreatedDomainEventHandler(ISender sender, IEventBus eventBus)
    : DomainEventHandler<{Entity}CreatedDomainEvent>          // NOT INotificationHandler
{
    public override async Task Handle({Entity}CreatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        Result<{Entity}Response> result = await sender.Send(new Get{Entity}Query(domainEvent.{Entity}Id), cancellationToken);
        if (result.IsFailure)
            throw new Common.Application.Exceptions.ApplicationException(nameof(Get{Entity}Query), result.Error);

        await eventBus.PublishAsync(
            new {Entity}CreatedIntegrationEvent(domainEvent.Id, domainEvent.OccurredOnUtc, result.Value...),
            cancellationToken);
    }
}
```
These run from `ProcessOutboxJob` (Quartz), are decorated with `IdempotentDomainEventHandler`, and publish to RabbitMQ via MassTransit (`IEventBus`). Reference: `src/Modules/Users/...Users.Application/Users/RegisterUser/UserRegisteredDomainEventHandler.cs`.

## Rules Summary
1. **No business logic in handlers** — if you're writing `if/else` that encodes business rules, move it to the domain entity
2. **Dapper for all reads** — never `DbSet<T>` in query handlers; snake_case tables, no schema prefix
3. **Always async** — `SaveChangesAsync` not `SaveChanges`; `OpenConnectionAsync` not `OpenConnection`
4. **All types internal sealed** — commands, queries, handlers, validators
5. **Use `ISender` for cross-handler calls** — never instantiate handlers directly
6. **Throw `Common.Application.Exceptions.ApplicationException` on integration event failure** — the outbox job catches it and retries
7. **Publish only via `IEventBus`** — never inject MassTransit's `IBus`/`IPublishEndpoint` here
8. **Integration events are full snapshots** — include everything consumers need; they cannot call back across services
