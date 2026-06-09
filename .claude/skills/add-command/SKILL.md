---
description: Add a CQRS command with handler and FluentValidation validator to a FoodDeliveryService module. Use when implementing a write/mutation operation.
disable-model-invocation: true
argument-hint: [ModuleName] [ActionEntity]
---

# Add Command

Arguments: `$ARGUMENTS` — format: `{ModuleName} {ActionEntity}` (e.g. `Orders PlaceOrder` or `Restaurants CreateRestaurant`)

Reference: `evently_source_code/evently/src/Modules/Events/Evently.Modules.Events.Application/Events/CreateEvent/`

## Files to Create

Path: `src/Modules/{ModuleName}/FoodDelivery.Modules.{ModuleName}.Application/{Domain}/{ActionEntity}/`

### 1. `{ActionEntity}Command.cs`
```csharp
using FoodDelivery.Common.Application.Messaging;

namespace FoodDelivery.Modules.{ModuleName}.Application.{Domain}.{ActionEntity};

public sealed record {ActionEntity}Command(
    // properties matching the operation inputs
    ) : ICommand<Guid>;  // use ICommand (no return) if no ID to return
```

### 2. `{ActionEntity}CommandHandler.cs`
```csharp
using FoodDelivery.Common.Application.Messaging;
using FoodDelivery.Common.Domain;
using FoodDelivery.Modules.{ModuleName}.Application.Abstractions.Data;
using FoodDelivery.Modules.{ModuleName}.Domain.{Domain}s;

namespace FoodDelivery.Modules.{ModuleName}.Application.{Domain}.{ActionEntity};

internal sealed class {ActionEntity}CommandHandler(
    I{Entity}Repository {entity}Repository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<{ActionEntity}Command, Guid>
{
    public async Task<Result<Guid>> Handle({ActionEntity}Command request, CancellationToken cancellationToken)
    {
        // 1. Fetch domain dependencies (repos, other aggregates needed)
        {Entity}? {entity} = await {entity}Repository.GetAsync(request.{Entity}Id, cancellationToken);
        if ({entity} is null)
            return Result.Failure<Guid>({Entity}Errors.NotFound(request.{Entity}Id));

        // 2. Call domain method — ALL business logic lives in the domain entity
        Result result = {entity}.{BusinessAction}(request.SomeParam);
        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        // 3. Persist
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return {entity}.Id;
    }
}
```

### 3. `{ActionEntity}CommandValidator.cs`
```csharp
using FluentValidation;

namespace FoodDelivery.Modules.{ModuleName}.Application.{Domain}.{ActionEntity};

internal sealed class {ActionEntity}CommandValidator : AbstractValidator<{ActionEntity}Command>
{
    public {ActionEntity}CommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.SomeId).NotEqual(Guid.Empty);
        // Add rules for each property
    }
}
```

## Rules
- Handler is `internal sealed` — never `public`
- Handler ONLY orchestrates: fetch → call domain method → persist. No if/else business logic
- Always `await unitOfWork.SaveChangesAsync(cancellationToken)` — never `SaveChanges()`
- Validator covers every property that can be invalid
- If creating a new aggregate, call the static factory: `{Entity}.Create(...)` — never `new {Entity}(...)`
