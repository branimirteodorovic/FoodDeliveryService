using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Modules.Users.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Users.Domain.Users;

namespace FoodDeliveryService.Users.Api.Seed;

/// <summary>
/// Seeds the Users-module <see cref="User"/> record for the initial administrator, aligned to the
/// Identity admin credential via a shared <see cref="AdminSeedOptions.IdentityId"/>. Runs once at
/// startup (after migrations), is idempotent and no-ops when "AdminSeed" is empty. Creating the
/// User with the Administrator role is what makes permission resolution (GetUserPermissionsRequest)
/// return the admin's permission set for the JWT issued by Identity.
/// </summary>
internal static class AdminSeeder
{
    public static async Task SeedAdminAsync(WebApplication app)
    {
        AdminSeedOptions options = app.Configuration
            .GetSection(AdminSeedOptions.SectionName)
            .Get<AdminSeedOptions>() ?? new AdminSeedOptions();

        ILogger logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(AdminSeeder));

        if (!options.IsEnabled)
        {
            logger.LogInformation(
                "AdminSeed is not configured — skipping administrator user seeding.");
            return;
        }

        using IServiceScope scope = app.Services.CreateScope();

        IServiceProvider services = scope.ServiceProvider;

        if (await AdminExistsAsync(services, options))
        {
            logger.LogInformation(
                "Administrator user '{Email}' already exists — skipping seeding.", options.Email);
            return;
        }

        var user = User.Create(
            options.Email,
            options.FirstName,
            options.LastName,
            options.IdentityId,
            Role.Administrator);

        services.GetRequiredService<IUserRepository>().Insert(user);

        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        logger.LogInformation(
            "Seeded administrator user '{Email}' (identity id {IdentityId}).",
            options.Email,
            options.IdentityId);
    }

    private static async Task<bool> AdminExistsAsync(IServiceProvider services, AdminSeedOptions options)
    {
        IDbConnectionFactory dbConnectionFactory = services.GetRequiredService<IDbConnectionFactory>();

        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        const string sql =
            """
            SELECT COUNT(1)
            FROM users
            WHERE identity_id = @IdentityId OR email = @Email
            """;

        int count = await connection.ExecuteScalarAsync<int>(
            sql,
            new { options.IdentityId, options.Email });

        return count > 0;
    }
}
