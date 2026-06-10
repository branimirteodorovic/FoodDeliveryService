---
description: Scaffold a complete new module for FoodDeliveryService. Use when adding a new bounded context or feature module.
disable-model-invocation: true
argument-hint: [ModuleName]
---

# Add Module: $ARGUMENTS

Scaffold all 5 projects for a new module named `$ARGUMENTS`.

Reference: `evently_source_code/evently/src/Modules/Events/` for the complete pattern.

## Projects to Create

| Project | Project References |
|---------|-------------------|
| `FoodDelivery.Modules.$ARGUMENTS.Domain` | `FoodDelivery.Common.Domain` only |
| `FoodDelivery.Modules.$ARGUMENTS.Application` | Common.Application, .Domain |
| `FoodDelivery.Modules.$ARGUMENTS.IntegrationEvents` | Common.Application only |
| `FoodDelivery.Modules.$ARGUMENTS.Infrastructure` | Common.Infrastructure, .Application, .IntegrationEvents |
| `FoodDelivery.Modules.$ARGUMENTS.Presentation` | Common.Presentation, .Application, .IntegrationEvents |

Add all 5 to `FoodDelivery.sln` and add Infrastructure + Presentation as references in `FoodDelivery.Api`.

## Required Files

### Domain project
```
AssemblyReference.cs
```
```csharp
namespace FoodDelivery.Modules.$ARGUMENTS.Domain;
public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
```

### Application project
```
AssemblyReference.cs
Abstractions/Data/IUnitOfWork.cs
```
```csharp
// IUnitOfWork.cs
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

### Infrastructure project
```
AssemblyReference.cs
Database/Schemas.cs
Database/$ARGUMENTSDbContext.cs
Outbox/Process$ARGUMENTSOutboxJob.cs       (copy from evently pattern)
Outbox/ConfigureProcess$ARGUMENTSOutboxJob.cs
Inbox/Process$ARGUMENTSInboxJob.cs         (copy from evently pattern)
Inbox/ConfigureProcess$ARGUMENTSInboxJob.cs
$ARGUMENTSModule.cs
```

**Schemas.cs:**
```csharp
namespace FoodDelivery.Modules.$ARGUMENTS.Infrastructure.Database;
internal static class Schemas
{
    internal const string $ARGUMENTS = "$ARGUMENTS_lowercase";
}
```

**$ARGUMENTSDbContext.cs** (snake_case + schema isolation):
```csharp
public sealed class $ARGUMENTSDbContext(DbContextOptions<$ARGUMENTSDbContext> options)
    : DbContext(options), IUnitOfWork
{
    internal DbSet<OutboxMessage> OutboxMessages { get; set; }
    internal DbSet<InboxMessage> InboxMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schemas.$ARGUMENTS);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
    }
}
```

**$ARGUMENTSModule.cs:**
```csharp
public static class $ARGUMENTSModule
{
    public static IServiceCollection Add$ARGUMENTSModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDomainEventHandlers();
        services.AddIntegrationEventHandlers();
        services.AddInfrastructure(configuration);
        services.AddEndpoints(Presentation.AssemblyReference.Assembly);
        return services;
    }

    private static void AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<$ARGUMENTSDbContext>((sp, options) =>
            options
                .UseNpgsql(configuration.GetConnectionString("Database"),
                    npgsql => npgsql.MigrationsHistoryTable(
                        HistoryRepository.DefaultTableName, Schemas.$ARGUMENTS))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<InsertOutboxMessagesInterceptor>()));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<$ARGUMENTSDbContext>());

        services.Configure<OutboxOptions>(configuration.GetSection("$ARGUMENTS:Outbox"));
        services.ConfigureOptions<ConfigureProcess$ARGUMENTSOutboxJob>();
        services.Configure<InboxOptions>(configuration.GetSection("$ARGUMENTS:Inbox"));
        services.ConfigureOptions<ConfigureProcess$ARGUMENTSInboxJob>();
    }

    private static void AddDomainEventHandlers(this IServiceCollection services)
    {
        Type[] handlers = Application.AssemblyReference.Assembly
            .GetTypes().Where(t => t.IsAssignableTo(typeof(IDomainEventHandler))).ToArray();
        foreach (Type handler in handlers)
        {
            services.TryAddScoped(handler);
            Type domainEvent = handler.GetInterfaces().Single(i => i.IsGenericType).GetGenericArguments().Single();
            services.Decorate(handler, typeof(IdempotentDomainEventHandler<>).MakeGenericType(domainEvent));
        }
    }

    private static void AddIntegrationEventHandlers(this IServiceCollection services)
    {
        Type[] handlers = Presentation.AssemblyReference.Assembly
            .GetTypes().Where(t => t.IsAssignableTo(typeof(IIntegrationEventHandler))).ToArray();
        foreach (Type handler in handlers)
        {
            services.TryAddScoped(handler);
            Type integrationEvent = handler.GetInterfaces().Single(i => i.IsGenericType).GetGenericArguments().Single();
            services.Decorate(handler, typeof(IdempotentIntegrationEventHandler<>).MakeGenericType(integrationEvent));
        }
    }
}
```

### Presentation project
```
AssemblyReference.cs
Permissions.cs
Tags.cs
```

### IntegrationEvents project
```
AssemblyReference.cs
```

## Register in FoodDelivery.Api/Program.cs
```csharp
builder.Services.Add$ARGUMENTSModule(builder.Configuration);
```

Add module configuration file: `src/API/FoodDelivery.Api/modules.$ARGUMENTS_lowercase.json`

## Create Initial Migration
```bash
dotnet ef migrations add Init --project src/Modules/$ARGUMENTS/FoodDelivery.Modules.$ARGUMENTS.Infrastructure --startup-project src/API/FoodDelivery.Api
```
