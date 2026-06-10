using System.Data.Common;
using FoodDeliveryService.Common.Application.Data;
using Npgsql;

namespace FoodDeliveryService.Common.Infrastructure.Data;

internal sealed class DbConnectionFactory(NpgsqlDataSource dataSource) : IDbConnectionFactory
{
    public async ValueTask<DbConnection> OpenConnectionAsync()
    {
        return await dataSource.OpenConnectionAsync();
    }
}
