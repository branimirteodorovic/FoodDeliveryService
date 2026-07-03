---
name: domain-layer-rules
description: Rules that apply when working in FoodDeliveryService Domain layer projects
paths: ["**/Domain/**/*.cs", "**/Modules/**/*.Domain/**/*.cs"]
---

# Domain Layer Rules

You are currently editing a **Domain layer** file. Domain projects contain the core business model and must be kept pure. Domain events raised here feed the outbox and ultimately other microservices — a missing `Raise(...)` silently breaks cross-service integration.

## Absolute Constraints

**No infrastructure dependencies.** Domain projects only reference `FoodDeliveryService.Common.Domain`.
These imports are FORBIDDEN in Domain projects:
- `Microsoft.EntityFrameworkCore` (any EF Core namespace)
- `MassTransit`, `RabbitMQ` (any messaging namespace)
- `MediatR` (any MediatR namespace)
- `Quartz`, `StackExchange.Redis`, `Dapper`
- Any NuGet package not referenced by `FoodDeliveryService.Common.Domain`

## Required Patterns

### Entity Structure
```csharp
public sealed class {Name} : Entity           // always sealed, always : Entity
{
    private {Name}() { }                      // private parameterless constructor (required)

    public Guid Id { get; private set; }      // private set on ALL properties
    public string Name { get; private set; }
}
```

### Factory Method
```csharp
public static Result<{Name}> Create(string name)  // static factory, returns Result<T>
{
    if (string.IsNullOrWhiteSpace(name))           // validate inputs
        return Result.Failure<{Name}>({Name}Errors.NameEmpty);

    var entity = new {Name} { Id = Guid.NewGuid(), Name = name };
    entity.Raise(new {Name}CreatedDomainEvent(entity.Id));  // always raise event
    return entity;
}
```

### Business Methods
```csharp
public Result {Action}(...)                   // returns Result or Result<T>
{
    if (/* invalid state check */)            // guard clause at top
        return Result.Failure({Name}Errors.InvalidState);

    // mutate state
    PropertyName = newValue;

    Raise(new {Action}DomainEvent(Id));       // ALWAYS raise domain event on state change

    return Result.Success();
}
```

### Errors Class
```csharp
public static class {Name}Errors             // static class in same folder as entity
{
    public static readonly Error NotFound =
        Error.NotFound("{Module}.{Name}NotFound", "{Name} was not found.");
    public static Error NotFoundById(Guid id) =>
        Error.NotFound("{Module}.{Name}NotFound", $"'{Name}' with id '{id}' was not found.");
}
```

### Value Objects
```csharp
public sealed record {Name}(decimal Amount, string Currency)  // sealed record = immutable
{
    public static Result<{Name}> Create(decimal amount, string currency) { ... }
}
```

### Repository Interface (lives in Domain, implemented in Infrastructure)
```csharp
public interface I{Name}Repository
{
    Task<{Name}?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    void Insert({Name} {name});
}
```

## Quick Reference
- Base classes: `Entity`, `DomainEvent`, `IDomainEvent` — from `FoodDeliveryService.Common.Domain`
- Result types: `Result`, `Result<T>`, `Error` — from `FoodDeliveryService.Common.Domain`
- Raise events: `protected void Raise(IDomainEvent domainEvent)` — inherited from `Entity`
- Reference example: `src/Modules/Users/FoodDeliveryService.Modules.Users.Domain/Users/User.cs`
- If the new state change matters to other services, remember the Application layer needs a `DomainEventHandler<T>` that publishes an integration event
