using FoodDeliveryService.Common.Infrastructure.Data;
using FoodDeliveryService.Modules.Support.Infrastructure.Database;

namespace FoodDeliveryService.Support.Api.Extensions;

internal static class MigrationExtensions
{
    /// <summary>
    /// Applies the module's pending EF Core migrations at startup, as the <c>fds_support_owner</c>
    /// account named by <c>ConnectionStrings:DatabaseMigrations</c> — the only DDL-capable
    /// credential this host holds. See
    /// <see cref="DatabaseMigrationExtensions.ApplyMigration{TDbContext}"/> and docs/security.md §4.
    /// </summary>
    public static void ApplyMigrations(this IApplicationBuilder app)
    {
        app.ApplyMigration<SupportDbContext>();
    }
}
