using System.Data.Common;
using FoodDeliveryService.Common.Application.Data;
using Npgsql;

namespace FoodDeliveryService.Common.Infrastructure.Data;

/// <summary>
/// Hands out open PostgreSQL connections from the service's pooled <see cref="NpgsqlDataSource"/>.
/// This is the entry point for all Dapper access: query handlers (CQRS read side) and the
/// outbox/inbox jobs use it, while EF Core (write side) manages its own connections.
/// </summary>
internal sealed class DbConnectionFactory(NpgsqlDataSource dataSource) : IDbConnectionFactory
{
    public async ValueTask<DbConnection> OpenConnectionAsync()
    {
        return await dataSource.OpenConnectionAsync();
    }
}
