using Duende.IdentityServer;
using FoodDeliveryService.Identity;
using FoodDeliveryService.Identity.Data;
using FoodDeliveryService.Identity.Users;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, loggerConfig) =>
    loggerConfig.ReadFrom.Configuration(context.Configuration));

string databaseConnectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("The 'Database' connection string is not configured.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(databaseConnectionString));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services
    .AddIdentityServer(options =>
    {
        // Deterministic issuer so machine-to-machine tokens validate across the
        // container network and the host mapping.
        options.IssuerUri = builder.Configuration["IdentityServer:IssuerUri"];

        options.Events.RaiseErrorEvents = true;
        options.Events.RaiseInformationEvents = true;
        options.Events.RaiseFailureEvents = true;
        options.Events.RaiseSuccessEvents = true;
    })
    .AddInMemoryIdentityResources(Config.IdentityResources)
    .AddInMemoryApiScopes(Config.ApiScopes)
    .AddInMemoryApiResources(Config.ApiResources)
    .AddInMemoryClients(Config.Clients(builder.Configuration))
    .AddAspNetIdentity<ApplicationUser>();

builder.Services.AddLocalApiAuthentication();

builder.Services.AddAuthorization(options =>
    options.AddPolicy(Config.UsersRegisterPolicy, policy =>
    {
        policy.AddAuthenticationSchemes(IdentityServerConstants.LocalApi.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("scope", "users:register");
    }));

builder.Services.AddHealthChecks()
    .AddNpgSql(databaseConnectionString);

WebApplication app = builder.Build();

await ApplyDatabaseMigrationsAsync(app);

app.UseSerilogRequestLogging();

app.UseIdentityServer();

app.UseAuthorization();

app.MapUserEndpoints();

app.MapHealthChecks("health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

await app.RunAsync();

static async Task ApplyDatabaseMigrationsAsync(WebApplication app)
{
    using IServiceScope scope = app.Services.CreateScope();

    ApplicationDbContext dbContext =
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // Creates the ASP.NET Core Identity schema on first run. Replace with
    // dbContext.Database.MigrateAsync() once EF Core migrations are added.
    await dbContext.Database.EnsureCreatedAsync();
}
