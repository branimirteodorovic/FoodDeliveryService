using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace FoodDeliveryService.Modules.Payments.Infrastructure.Database;

/// <summary>
/// Design-time factory used only by `dotnet ef` (migrations/scaffolding). It builds the DbContext
/// directly instead of booting the API host, because the host's DI graph depends on live Redis and
/// RabbitMQ connections that are not available at design time.
/// </summary>
internal sealed class PaymentsDbContextFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args)
    {
        // No password: this connection is never opened. `migrations add` only needs the Npgsql
        // provider to emit SQL, and migrations are applied at runtime via app.ApplyMigrations()
        // using the host's real configuration — not `dotnet ef database update` through here.
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder
        {
            Host = "localhost",
            Database = "fooddeliveryservice_payments",
            Username = "postgres"
        };

        DbContextOptions<PaymentsDbContext> options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseNpgsql(
                connectionStringBuilder.ConnectionString,
                npgsqlOptions => npgsqlOptions.MigrationsHistoryTable(HistoryRepository.DefaultTableName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new PaymentsDbContext(options);
    }
}
