using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace FoodDeliveryService.Modules.RealTime.Infrastructure.Database;

/// <summary>
/// Design-time factory used only by `dotnet ef` (migrations/scaffolding). It builds the DbContext
/// directly instead of booting the API host, because the host's DI graph depends on a live Redis
/// connection (the SignalR backplane) that isn't available at design time. The connection string
/// here is never actually opened — `migrations add` only needs the Npgsql provider to generate SQL —
/// so a placeholder host is sufficient.
/// </summary>
internal sealed class RealTimeDbContextFactory : IDesignTimeDbContextFactory<RealTimeDbContext>
{
    public RealTimeDbContext CreateDbContext(string[] args)
    {
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder
        {
            Host = "localhost",
            Database = "fooddeliveryservice_realtime",
            Username = "postgres"
        };

        DbContextOptions<RealTimeDbContext> options = new DbContextOptionsBuilder<RealTimeDbContext>()
            .UseNpgsql(
                connectionStringBuilder.ConnectionString,
                npgsqlOptions => npgsqlOptions.MigrationsHistoryTable(HistoryRepository.DefaultTableName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new RealTimeDbContext(options);
    }
}
