---
description: Add a CQRS query with Dapper handler and response DTO to a FoodDeliveryService module. Use when implementing a read operation.
disable-model-invocation: true
argument-hint: [ModuleName] [EntityName]
---

# Add Query

Arguments: `$ARGUMENTS` — format: `{ModuleName} {EntityName}` (e.g. `Orders Order` or `Restaurants Restaurant`)

Reference: `evently_source_code/evently/src/Modules/Events/Evently.Modules.Events.Application/Categories/GetCategories/`

## Files to Create

Path: `src/Modules/{ModuleName}/FoodDelivery.Modules.{ModuleName}.Application/{Domain}s/Get{Entity}/`

### 1. `Get{Entity}Query.cs`
```csharp
using FoodDelivery.Common.Application.Messaging;

namespace FoodDelivery.Modules.{ModuleName}.Application.{Domain}s.Get{Entity};

public sealed record Get{Entity}Query(Guid {Entity}Id) : IQuery<{Entity}Response>;
```

For a list query:
```csharp
public sealed record Get{Entities}Query : IQuery<IReadOnlyCollection<{Entity}Response>>;
```

### 2. `{Entity}Response.cs`
```csharp
namespace FoodDelivery.Modules.{ModuleName}.Application.{Domain}s.Get{Entity};

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
using FoodDelivery.Common.Application.Data;
using FoodDelivery.Common.Application.Messaging;
using FoodDelivery.Common.Domain;
using FoodDelivery.Modules.{ModuleName}.Domain.{Domain}s;

namespace FoodDelivery.Modules.{ModuleName}.Application.{Domain}s.Get{Entity};

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
             FROM {schema}.{table}
             WHERE id = @{Entity}Id
             """;

        {Entity}Response? response = await connection.QuerySingleOrDefaultAsync<{Entity}Response>(
            sql, new {{ request.{Entity}Id }});

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
            FROM {schema}.{table}
            ORDER BY name
            """;

        List<{Entity}Response> responses = (await connection.QueryAsync<{Entity}Response>(sql)).AsList();
        return responses;
    }
}
```

## Rules
- **NEVER use EF Core DbSet in query handlers** — always Dapper via `IDbConnectionFactory`
- Use `nameof(ResponseType.Property)` for column aliases to keep SQL refactor-safe
- Response types are `sealed record` (or `sealed class`) — NOT domain entities
- Column aliases must match response property names exactly (case-sensitive with snake_case DB columns)
- Schema comes from `Schemas.{ModuleName}` constant in Infrastructure — use the string value in raw SQL
