using FoodDeliveryService.Identity.Data;
using Microsoft.AspNetCore.Identity;

namespace FoodDeliveryService.Identity.Seed;

/// <summary>
/// Creates the initial Administrator credential in Identity from configuration (see
/// <see cref="AdminSeedOptions"/>). Runs once at startup, is idempotent (no-ops if the account
/// already exists) and no-ops entirely when the "AdminSeed" section is empty — which is the case
/// in production, forcing admins to be provisioned via a real secret rather than a committed default.
/// The account is created active (email confirmed) and NOT invited, using a well-known
/// <see cref="AdminSeedOptions.IdentityId"/> that the Users module reuses for its matching User row.
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
                "AdminSeed is not configured — skipping administrator seeding.");
            return;
        }

        using IServiceScope scope = app.Services.CreateScope();

        UserManager<ApplicationUser> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        ApplicationUser? existing =
            await userManager.FindByIdAsync(options.IdentityId)
            ?? await userManager.FindByEmailAsync(options.Email);

        if (existing is not null)
        {
            logger.LogInformation(
                "Administrator '{Email}' already exists — skipping seeding.", options.Email);
            return;
        }

        var admin = new ApplicationUser
        {
            Id = options.IdentityId,
            UserName = options.Email,
            Email = options.Email,
            FirstName = options.FirstName,
            LastName = options.LastName,
            EmailConfirmed = true
        };

        IdentityResult result = await userManager.CreateAsync(admin, options.Password);

        if (result.Succeeded)
        {
            logger.LogInformation(
                "Seeded administrator '{Email}' (id {IdentityId}).",
                options.Email,
                options.IdentityId);
            return;
        }

        string errors = string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
        logger.LogError("Failed to seed administrator '{Email}': {Errors}", options.Email, errors);
    }
}
