using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Common.Infrastructure.Data;

/// <summary>
/// Feature 3.7 Milestone C. The startup migration path, and the one place in a module host that is
/// allowed to hold a DDL-capable credential.
/// <para>
/// Migrations run in-process, at boot, from the same host that then serves traffic, so a single
/// connection string cannot be both DDL-capable at startup and DML-only afterwards. The split is
/// therefore in configuration: <c>ConnectionStrings:Database</c> is the least-privilege
/// <c>fds_{service}_app</c> account every request-serving pool holds, and
/// <see cref="MigrationsConnectionStringName"/> is the <c>fds_{service}_owner</c> account used
/// here and nowhere else. The privileged credential never reaches the DI-registered
/// <see cref="DbContext"/>, the Npgsql data source Dapper reads through, or the outbox/inbox jobs.
/// </para>
/// <para>
/// The context is built by hand rather than resolved from DI for exactly that reason: the
/// registered one is bound to the app connection string. Its options mirror what every
/// <c>{Module}Module</c> registers — the same provider, the same
/// <see cref="HistoryRepository.DefaultTableName"/> (which the snake-case convention would
/// otherwise rename, pointing the migration at a history table that does not exist) and the same
/// naming convention. It does NOT add the outbox interceptor: nothing raises a domain event during
/// a migration.
/// </para>
/// </summary>
public static class DatabaseMigrationExtensions
{
    /// <summary>
    /// The owner credential. Falls back to <c>ConnectionStrings:Database</c> when absent, which is
    /// what lets the integration fixtures keep pointing a whole host at one superuser
    /// Testcontainers connection without knowing this split exists.
    /// </summary>
    public const string MigrationsConnectionStringName = "DatabaseMigrations";

    /// <summary>
    /// Applies <typeparamref name="TDbContext"/>'s pending migrations using the owner credential.
    /// </summary>
    /// <remarks>
    /// <typeparamref name="TDbContext"/> must expose the single
    /// <c>DbContextOptions&lt;TDbContext&gt;</c> constructor every context in this solution has.
    /// </remarks>
    public static void ApplyMigration<TDbContext>(this IApplicationBuilder app)
        where TDbContext : DbContext
    {
        var configuration = app.ApplicationServices.GetRequiredService<IConfiguration>();

        // Blank, not just absent: appsettings.json declares the key with an empty value so the
        // shape is visible, and a host that only sets Database would otherwise fall through to an
        // empty connection string instead of the fallback.
        string? connectionString = configuration.GetConnectionString(MigrationsConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = configuration.GetConnectionString("Database");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Neither the {MigrationsConnectionStringName} nor the Database connection string was found.");
        }

        DbContextOptions<TDbContext> options = new DbContextOptionsBuilder<TDbContext>()
            .UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsHistoryTable(HistoryRepository.DefaultTableName))
            .UseSnakeCaseNamingConvention()
            .Options;

        using var context = (TDbContext)Activator.CreateInstance(typeof(TDbContext), options)!;

        context.Database.Migrate();
    }
}
