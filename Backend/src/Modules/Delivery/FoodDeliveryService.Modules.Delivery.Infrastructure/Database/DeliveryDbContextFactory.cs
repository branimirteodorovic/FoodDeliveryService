using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Database;

/// <summary>
/// Design-time factory used only by `dotnet ef` (migrations/scaffolding). It builds the DbContext
/// directly instead of booting the API host, because the host's DI graph now depends on a live
/// Redis connection (the driver location store) that isn't available at design time. The
/// connection string here is never actually opened — `migrations add` only needs the Npgsql
/// provider to generate SQL — so a placeholder host is sufficient.
/// </summary>
internal sealed class DeliveryDbContextFactory : IDesignTimeDbContextFactory<DeliveryDbContext>
{
    public DeliveryDbContext CreateDbContext(string[] args)
    {
        // No password: this connection is never opened. `migrations add` only needs the Npgsql
        // provider to emit SQL, and migrations are applied at runtime via app.ApplyMigrations()
        // using the host's real configuration — not `dotnet ef database update` through this factory.
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder
        {
            Host = "localhost",
            Database = "fooddeliveryservice_delivery",
            Username = "postgres"
        };

        DbContextOptions<DeliveryDbContext> options = new DbContextOptionsBuilder<DeliveryDbContext>()
            .UseNpgsql(
                connectionStringBuilder.ConnectionString,
                npgsqlOptions => npgsqlOptions.MigrationsHistoryTable(HistoryRepository.DefaultTableName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new DeliveryDbContext(options);
    }
}
