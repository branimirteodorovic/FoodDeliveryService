using Duende.IdentityServer;
using FoodDeliveryService.Identity;
using FoodDeliveryService.Identity.Data;
using FoodDeliveryService.Identity.Users;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Identity service (:18080) — the platform's OpenID Connect / OAuth2 authorization server,
// built on Duende IdentityServer + ASP.NET Core Identity. It issues the JWTs that the YARP
// gateway and every microservice validate. It is a plain host, NOT a module: no MassTransit,
// no outbox — its only extra surface is the local API (api/users) used by the Users module
// to provision credentials.

var builder = WebApplication.CreateBuilder(args);

// Serilog structured logging (Console + Seq sinks, configured in appsettings "Serilog").
builder.Host.UseSerilog((context, loggerConfig) =>
    loggerConfig.ReadFrom.Configuration(context.Configuration));

string databaseConnectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("The 'Database' connection string is not configured.");

// EF Core on PostgreSQL (Npgsql provider) with Identity's own database
// (fooddeliveryservice_identity) — separate from every module database.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(databaseConnectionString));

// ASP.NET Core Identity: stores user credentials (password hashes, emails) and handles
// password validation. Duende sits on top of this store to issue tokens.
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// Duende IdentityServer: token issuance (OpenID Connect discovery, /connect/token, etc.).
// Clients, scopes and resources are defined in-memory in Config.cs — there are two clients:
// a public one for end users and a confidential client-credentials one that the Users module
// uses to call the registration local API.
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

// Duende "local API" authentication: lets this host protect its own endpoints (api/users)
// with tokens it issued itself.
builder.Services.AddLocalApiAuthentication();

// Only callers holding the users:register scope (the confidential client used by the Users
// module's DuendeIdentityClient) may provision users.
builder.Services.AddAuthorization(options =>
    options.AddPolicy(Config.UsersRegisterPolicy, policy =>
    {
        policy.AddAuthenticationSchemes(IdentityServerConstants.LocalApi.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("scope", "users:register");
    }));

// Health check for the Identity database; exposed at GET /health — the module services also
// probe this URL via their own "Duende" health check.
builder.Services.AddHealthChecks()
    .AddNpgSql(databaseConnectionString);

WebApplication app = builder.Build();

await ApplyDatabaseMigrationsAsync(app);

app.UseSerilogRequestLogging();

// Duende middleware: serves the OpenID Connect discovery document
// (/.well-known/openid-configuration), token endpoint and the rest of the protocol surface.
app.UseIdentityServer();

app.UseAuthorization();

// Local API (api/users) called by the Users module to create credentials during registration —
// the ONLY sanctioned HTTP call between services in this system.
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
