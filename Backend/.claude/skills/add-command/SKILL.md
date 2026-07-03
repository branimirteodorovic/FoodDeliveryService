---
description: Add a CQRS command with handler and FluentValidation validator to a FoodDeliveryService module. Use when implementing a write/mutation operation.
disable-model-invocation: true
argument-hint: [ModuleName] [ActionEntity]
---

# Add Command

Arguments: `$ARGUMENTS` — format: `{ModuleName} {ActionEntity}` (e.g. `Orders PlaceOrder` or `Restaurants CreateRestaurant`)

Reference: `src/Modules/Users/FoodDeliveryService.Modules.Users.Application/Users/RegisterUser/`

## Files to Create

Path: `src/Modules/{ModuleName}/FoodDeliveryService.Modules.{ModuleName}.Application/{Entity}s/{ActionEntity}/`

### 1. `{ActionEntity}Command.cs`
```csharp
using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.{ModuleName}.Application.{Entity}s.{ActionEntity};

public sealed record {ActionEntity}Command(
    // properties matching the operation inputs
    ) : ICommand<Guid>;  // use ICommand (no return) if no ID to return
```

### 2. `{ActionEntity}CommandHandler.cs`
```csharp
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.{ModuleName}.Application.Abstractions.Data;
using FoodDeliveryService.Modules.{ModuleName}.Domain.{Entity}s;

namespace FoodDeliveryService.Modules.{ModuleName}.Application.{Entity}s.{ActionEntity};

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
            return Result.Failure<Guid>({Entity}Errors.NotFoundById(request.{Entity}Id));

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

namespace FoodDeliveryService.Modules.{ModuleName}.Application.{Entity}s.{ActionEntity};

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

### 4. Endpoint (Presentation project) — if the command is triggered over HTTP
```csharp
internal sealed class {ActionEntity} : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("{module-route}", async (Request request, ISender sender) =>
        {
            Result<Guid> result = await sender.Send(new {ActionEntity}Command(...));
            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.{ActionEntity})
        .WithTags(Tags.{ModuleName});
    }
}
```
The route must fall under the module's YARP prefix (`{module}/**`) — no gateway change needed then. Commands can also be triggered by integration event handlers (inbox) instead of HTTP.

## Rules
- Handler is `internal sealed` — never `public`
- Handler ONLY orchestrates: fetch → call domain method → persist. No if/else business logic
- Always `await unitOfWork.SaveChangesAsync(cancellationToken)` — never `SaveChanges()`
- Validator covers every property that can be invalid
- If creating a new aggregate, call the static factory: `{Entity}.Create(...)` — never `new {Entity}(...)`
- If the state change matters to other services, add a domain event + integration event (`/add-domain-event`)
