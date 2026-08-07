using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace FoodDeliveryService.Modules.FraudDetection.Infrastructure.Database;

/// <summary>
/// Design-time factory used only by `dotnet ef` (migrations/scaffolding). It builds the DbContext
/// directly instead of booting the API host, whose DI graph needs live Redis and RabbitMQ
/// connections that are not available at design time. Mirrors DeliveryDbContextFactory.
/// </summary>
internal sealed class FraudDetectionDbContextFactory : IDesignTimeDbContextFactory<FraudDetectionDbContext>
{
    public FraudDetectionDbContext CreateDbContext(string[] args)
    {
        // No password: this connection is never opened. `migrations add` only needs the Npgsql
        // provider to emit SQL, and migrations are applied at runtime via app.ApplyMigrations()
        // using the host's real configuration.
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder
        {
            Host = "localhost",
            Database = "fooddeliveryservice_frauddetection",
            Username = "postgres"
        };

        DbContextOptions<FraudDetectionDbContext> options = new DbContextOptionsBuilder<FraudDetectionDbContext>()
            .UseNpgsql(
                connectionStringBuilder.ConnectionString,
                npgsqlOptions => npgsqlOptions.MigrationsHistoryTable(HistoryRepository.DefaultTableName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new FraudDetectionDbContext(options);
    }
}
