---
description: Add a CQRS query with Dapper handler and response DTO to a FoodDeliveryService module. Use when implementing a read operation.
disable-model-invocation: true
argument-hint: [ModuleName] [EntityName]
---

# Add Query

Arguments: `$ARGUMENTS` — format: `{ModuleName} {EntityName}` (e.g. `Orders Order` or `Restaurants Restaurant`)

Reference: `src/Modules/Users/FoodDeliveryService.Modules.Users.Application/Users/GetUser/`

## Files to Create

Path: `src/Modules/{ModuleName}/FoodDeliveryService.Modules.{ModuleName}.Application/{Entity}s/Get{Entity}/`

### 1. `Get{Entity}Query.cs`
```csharp
using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.{ModuleName}.Application.{Entity}s.Get{Entity};

public sealed record Get{Entity}Query(Guid {Entity}Id) : IQuery<{Entity}Response>;
```

For a list query:
```csharp
public sealed record Get{Entities}Query : IQuery<IReadOnlyCollection<{Entity}Response>>;
```

### 2. `{Entity}Response.cs`
```csharp
namespace FoodDeliveryService.Modules.{ModuleName}.Application.{Entity}s.Get{Entity};

public sealed record {Entity}Response(
    Guid Id,
    string Name
    // ... all fields needed by the API consumer
);
```

### 3. `Get{Entity}QueryHandler.cs`
```csharp
using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.{ModuleName}.Domain.{Entity}s;

namespace FoodDeliveryService.Modules.{ModuleName}.Application.{Entity}s.Get{Entity};

internal sealed class Get{Entity}QueryHandler(IDbConnectionFactory dbConnectionFactory)
    : IQueryHandler<Get{Entity}Query, {Entity}Response>
{
    public async Task<Result<{Entity}Response>> Handle(
        Get{Entity}Query request,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        const string sql =
            $"""
             SELECT
                 id AS {nameof({Entity}Response.Id)},
                 name AS {nameof({Entity}Response.Name)}
             FROM {table}
             WHERE id = @{Entity}Id
             """;

        {Entity}Response? response = await connection.QuerySingleOrDefaultAsync<{Entity}Response>(
            sql, new { request.{Entity}Id });

        return response is not null
            ? response
            : Result.Failure<{Entity}Response>({Entity}Errors.NotFoundById(request.{Entity}Id));
    }
}
```

For a list query handler:
```csharp
internal sealed class Get{Entities}QueryHandler(IDbConnectionFactory dbConnectionFactory)
    : IQueryHandler<Get{Entities}Query, IReadOnlyCollection<{Entity}Response>>
{
    public async Task<Result<IReadOnlyCollection<{Entity}Response>>> Handle(
        Get{Entities}Query request, CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        const string sql = $"""
            SELECT id AS {nameof({Entity}Response.Id)}, name AS {nameof({Entity}Response.Name)}
            FROM {table}
            ORDER BY name
            LIMIT @Limit
            """;

        List<{Entity}Response> responses = (await connection.QueryAsync<{Entity}Response>(sql, request)).AsList();
        return responses;
    }
}
```

### 4. Endpoint (Presentation project)
```csharp
internal sealed class Get{Entity} : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("{module-route}/{id:guid}", async (Guid id, ISender sender) =>
        {
            Result<{Entity}Response> result = await sender.Send(new Get{Entity}Query(id));
            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.Get{Entity})
        .WithTags(Tags.{ModuleName});
    }
}
```

## Rules
- **NEVER use EF Core DbSet in query handlers** — always Dapper via `IDbConnectionFactory`
- Tables are snake_case in the default (`public`) schema of the service's OWN database — no schema prefix, no cross-service queries
- If you need data owned by another service, it must be replicated into this module via integration events first
- Use `nameof(ResponseType.Property)` for column aliases to keep SQL refactor-safe
- Response types are `sealed record` — NOT domain entities
- Parameterize everything (including `LIMIT`) — never interpolate user input into SQL
