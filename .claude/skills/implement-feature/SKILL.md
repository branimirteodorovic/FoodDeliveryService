---
description: Use when the user describes a feature, user story, or requirement to implement. Implements the feature following FoodDeliveryService DDD/CQRS modular monolith patterns (MediatR, DAPR, Duende IdentityServer, EF Core, Dapper). Automatically triggered when the user pastes a feature description or asks to implement something new.
---

# Implement Feature

## Pre-flight (always do these steps first)
1. Read `CLAUDE.md` to confirm current rules and patterns
2. Identify which module this feature belongs to (or whether a new module is needed)
3. Study the **closest evently equivalent** pattern:
   - Domain: `evently_source_code/evently/src/Modules/{ClosestModule}/Evently.Modules.{ClosestModule}.Domain/`
   - Application: `evently_source_code/evently/src/Modules/{ClosestModule}/Evently.Modules.{ClosestModule}.Application/`
   - Presentation: `evently_source_code/evently/src/Modules/{ClosestModule}/Evently.Modules.{ClosestModule}.Presentation/`
   - Module registration: `evently_source_code/evently/src/Modules/{ClosestModule}/Evently.Modules.{ClosestModule}.Infrastructure/EventsModule.cs`

## Feature Analysis
Before writing code, answer:
- What aggregate(s) are involved?
- What commands (writes) are needed?
- What queries (reads) are needed?
- What domain events are triggered?
- Are any integration events needed (cross-module)?
- Which module owns this feature?

## Implementation Order

### Step 1 — Domain Layer (`src/Modules/{Module}/FoodDelivery.Modules.{Module}.Domain/`)

**Aggregate / Entity:**
```csharp
public sealed class {Entity} : Entity
{
    private {Entity}() { }  // EF Core requires private parameterless ctor

    public Guid Id { get; private set; }
    // All properties: private set

    public static Result<{Entity}> Create(...) // Factory method — returns Result<T> if creation can fail
    {
        // Guard clauses → return Result.Failure<{Entity}>({Entity}Errors.XyzError)
        var entity = new {Entity} { Id = Guid.NewGuid(), ... };
        entity.Raise(new {Entity}CreatedDomainEvent(entity.Id));
        return entity;
    }

    public Result {BusinessAction}(...) // Business methods
    {
        if (/* invalid state */) return Result.Failure({Entity}Errors.InvalidState);
        // mutate state
        Raise(new {Action}DomainEvent(Id));
        return Result.Success();
    }
}
```

**Errors class** (same folder as entity):
```csharp
public static class {Entity}Errors
{
    public static readonly Error NotFound = Error.NotFound("{Module}.{Entity}NotFound", "...");
    public static readonly Error AlreadyExists = Error.Conflict("{Module}.{Entity}AlreadyExists", "...");
    public static Error NotFoundById(Guid id) => Error.NotFound("{Module}.{Entity}NotFound", $"... {id}");
}
```

**Domain events** (one file per event):
```csharp
public sealed class {Entity}CreatedDomainEvent(Guid {entity}Id) : DomainEvent
{
    public Guid {Entity}Id { get; init; } = {entity}Id;
}
```

**Repository interface** (in Domain):
```csharp
public interface I{Entity}Repository
{
    Task<{Entity}?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    void Insert({Entity} {entity});
}
```

### Step 2 — Application Layer (`src/Modules/{Module}/FoodDelivery.Modules.{Module}.Application/`)

**Command** (one file per command):
```csharp
public sealed record Create{Entity}Command(...) : ICommand<Guid>;
```

**Command Handler:**
```csharp
internal sealed class Create{Entity}CommandHandler(
    I{Dependency}Repository dependencyRepository,
    I{Entity}Repository {entity}Repository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<Create{Entity}Command, Guid>
{
    public async Task<Result<Guid>> Handle(Create{Entity}Command request, CancellationToken cancellationToken)
    {
        // 1. Fetch domain dependencies
        // 2. Call domain factory/method — NO business logic here
        Result<{Entity}> result = {Entity}.Create(...);
        if (result.IsFailure) return Result.Failure<Guid>(result.Error);
        // 3. Persist
        {entity}Repository.Insert(result.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result.Value.Id;
    }
}
```

**Validator:**
```csharp
internal sealed class Create{Entity}CommandValidator : AbstractValidator<Create{Entity}Command>
{
    public Create{Entity}CommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
    }
}
```

**Query Handler (Dapper — NEVER EF Core for reads):**
```csharp
internal sealed class Get{Entity}QueryHandler(IDbConnectionFactory dbConnectionFactory)
    : IQueryHandler<Get{Entity}Query, {Entity}Response>
{
    public async Task<Result<{Entity}Response>> Handle(Get{Entity}Query request, CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();
        const string sql = $"""
            SELECT id AS {nameof({Entity}Response.Id)}, ...
            FROM {schema}.{table}
            WHERE id = @{nameof(request.{Entity}Id)}
            """;
        var response = await connection.QuerySingleOrDefaultAsync<{Entity}Response>(sql, request);
        return response ?? Result.Failure<{Entity}Response>({Entity}Errors.NotFoundById(request.{Entity}Id));
    }
}
```

**Domain Event Handler** (publishes integration event):
```csharp
internal sealed class {Entity}CreatedDomainEventHandler(ISender sender, IEventBus eventBus)
    : DomainEventHandler<{Entity}CreatedDomainEvent>
{
    public override async Task Handle({Entity}CreatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        Result<{Entity}Response> result = await sender.Send(new Get{Entity}Query(domainEvent.{Entity}Id), cancellationToken);
        if (result.IsFailure) throw new FoodDeliveryException(nameof(Get{Entity}Query), result.Error);

        await eventBus.PublishAsync(
            new {Entity}CreatedIntegrationEvent(domainEvent.Id, domainEvent.OccurredOnUtc, result.Value...),
            cancellationToken);
    }
}
```

### Step 3 — Integration Events (`src/Modules/{Module}/FoodDelivery.Modules.{Module}.IntegrationEvents/`)

```csharp
public sealed class {Entity}CreatedIntegrationEvent : IntegrationEvent
{
    public {Entity}CreatedIntegrationEvent(Guid id, DateTime occurredOnUtc, Guid {entity}Id, ...)
        : base(id, occurredOnUtc)
    {
        {Entity}Id = {entity}Id;
        // ... snapshot of data needed by other modules
    }
    public Guid {Entity}Id { get; init; }
}
```

### Step 4 — Infrastructure Layer (`src/Modules/{Module}/FoodDelivery.Modules.{Module}.Infrastructure/`)

**EF Core configuration:**
```csharp
internal sealed class {Entity}Configuration : IEntityTypeConfiguration<{Entity}>
{
    public void Configure(EntityTypeBuilder<{Entity}> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.ToTable("{table}", Schemas.{Module});
    }
}
```

**Repository:**
```csharp
internal sealed class {Entity}Repository(I{Module}DbContext context) : I{Entity}Repository
{
    public async Task<{Entity}?> GetAsync(Guid id, CancellationToken ct) =>
        await context.{Entities}.SingleOrDefaultAsync(e => e.Id == id, ct);

    public void Insert({Entity} {entity}) => context.{Entities}.Add({entity});
}
```

**Register in `{Module}Module.cs` AddInfrastructure():**
```csharp
services.AddScoped<I{Entity}Repository, {Entity}Repository>();
```

### Step 5 — Presentation Layer (`src/Modules/{Module}/FoodDelivery.Modules.{Module}.Presentation/`)

**Endpoint:**
```csharp
internal sealed class Create{Entity} : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("{route}", async (Request request, ISender sender) =>
        {
            Result<Guid> result = await sender.Send(new Create{Entity}Command(...));
            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.Modify{Entity})
        .WithTags(Tags.{Module});
    }

    internal sealed class Request { /* input properties */ }
}
```

**Integration event consumer** (if this module reacts to another module's events):
```csharp
internal sealed class {Entity}CreatedIntegrationEventHandler(ISender sender) : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/dapr/subscribe/{topic}",
            async ([FromBody] {Entity}CreatedIntegrationEvent @event, ISender sender) =>
            {
                Result result = await sender.Send(new Create{LocalEntity}Command(@event.{Entity}Id, ...));
                if (result.IsFailure) throw new FoodDeliveryException(nameof(Create{LocalEntity}Command), result.Error);
                return Results.Ok();
            })
            .WithTopic("fooddelivery-pubsub", nameof({Entity}CreatedIntegrationEvent));
    }
}
```

## Post-Implementation Checklist
- [ ] `dotnet build` — zero compilation errors
- [ ] Domain entity has no EF Core / DAPR / MediatR using statements
- [ ] Query handlers use `IDbConnectionFactory` + Dapper (grep for `DbSet` in query files)
- [ ] All commands/queries return `Result<T>`
- [ ] Domain event raised in every state-changing method
- [ ] Module registration updated in `{Module}Module.cs`
- [ ] `IEndpoint` implementations registered via `AddEndpoints(Presentation.AssemblyReference.Assembly)`
