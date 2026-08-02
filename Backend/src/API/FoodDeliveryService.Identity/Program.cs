using Duende.IdentityServer;
using FoodDeliveryService.Identity;
using FoodDeliveryService.Identity.Data;
using FoodDeliveryService.Identity.Seed;
using FoodDeliveryService.Common.Presentation.Correlation;
using FoodDeliveryService.Common.Presentation.Health;
using FoodDeliveryService.Common.Presentation.Telemetry;
using FoodDeliveryService.Identity.OpenTelemetry;
using FoodDeliveryService.Identity.Users;
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

// OpenTelemetry traces + metrics → OTLP exporter (:4317; traces browsable in Jaeger at :16686) —
// the same baseline the Gateway and, through AddInfrastructure, the six module hosts get.
// Until this call, Identity emitted no telemetry at all: the token endpoint sits on the critical
// path of every authenticated request in the system and was a blank gap in every distributed trace,
// so a slow login looked like a slow *caller*. Its spans now join the trace the gateway started.
builder.Services.AddHostTelemetry(DiagnosticsConfig.ServiceName);

string databaseConnectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("The 'Database' connection string is not configured.");

// EF Core on PostgreSQL (Npgsql provider) with Identity's own database
// (fooddeliveryservice_identity) — separate from every module database.
// EnableRetryOnFailure adds a retrying execution strategy so the startup schema bootstrap
// self-heals when Postgres is still initializing (transient "57P03: the database system is
// starting up") instead of crashing the host on first boot.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(
        databaseConnectionString,
        npgsqlOptions => npgsqlOptions.EnableRetryOnFailure()));

// ASP.NET Core Identity: stores user credentials (password hashes, emails) and handles
// password validation. Duende sits on top of this store to issue tokens.
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;

        if (builder.Environment.IsDevelopment())
        {
            // Relax password strength locally so the intentionally weak dev admin password
            // (see appsettings.Development.json "AdminSeed") can be seeded. Real environments
            // keep the strong defaults below.
            options.Password.RequiredLength = 1;
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
        }
        else
        {
            options.Password.RequiredLength = 8;
        }
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// Lifespan of the one-time activation tokens minted for invited accounts (GeneratePasswordResetToken).
// A few days gives invitees time to accept; expired links require the admin to re-issue the invitation.
builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
    options.TokenLifespan = TimeSpan.FromDays(3));

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
// with tokens it issued itself. ExpectedScope must be "users:register" — the default
// ("IdentityServerApi") is never granted to any client here, so the default would reject every
// caller, including the confidential client's otherwise-valid users:register token.
builder.Services
    .AddAuthentication(IdentityServerConstants.LocalApi.AuthenticationScheme)
    .AddLocalApi(IdentityServerConstants.LocalApi.AuthenticationScheme, options =>
    {
        options.ExpectedScope = Config.UsersRegisterScope;
    });

// Only callers holding the users:register scope (the confidential client used by the Users
// module's DuendeIdentityClient) may provision users.
builder.Services.AddAuthorization(options =>
    options.AddPolicy(Config.UsersRegisterPolicy, policy =>
    {
        policy.AddAuthenticationSchemes(IdentityServerConstants.LocalApi.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("scope", "users:register");
    }));

// Identity's readiness set is its own database and nothing else — it depends on no other service.
// The module hosts probe the aggregate GET /health below via their own "Duende" health check, so an
// Identity database outage propagates to their readiness too. See docs/health-probe-contract.md.
builder.Services.AddHealthChecks()
    .AddLivenessCheck()
    .AddNpgSql(databaseConnectionString, tags: [HealthCheckTags.Ready]);

WebApplication app = builder.Build();

await ApplyDatabaseMigrationsAsync(app);

// Config-driven initial-administrator seed (idempotent; no-ops when "AdminSeed" is empty).
await AdminSeeder.SeedAdminAsync(app);

// Identity's first log correlation. It had no LogContext middleware at all, so token issuance —
// the one hop every authenticated request in the system passes through — logged nothing that could
// be tied back to the request that caused it: a failed login in Seq was an island. Milestone A gave
// this host its first spans; this gives their ids to its logs, and echoes the Gateway's correlation
// id on the token response.
app.UseRequestCorrelation();

app.UseSerilogRequestLogging();

// Duende middleware: serves the OpenID Connect discovery document
// (/.well-known/openid-configuration), token endpoint and the rest of the protocol surface.
app.UseIdentityServer();

app.UseAuthorization();

// Local API (api/users) called by the Users module to create credentials during registration —
// the ONLY sanctioned HTTP call between services in this system.
app.MapUserEndpoints();

// GET /health/live + /health/ready + /health, the same contract every service exposes.
app.MapHealthProbes();

await app.RunAsync();

static async Task ApplyDatabaseMigrationsAsync(WebApplication app)
{
    using IServiceScope scope = app.Services.CreateScope();

    ApplicationDbContext dbContext =
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // Applies EF Core migrations (Data/Migrations) at startup — mirrors the modules'
    // app.ApplyMigrations(). Unlike EnsureCreated this evolves the schema, so changes to
    // ApplicationUser (e.g. MustChangePassword) ship as new migrations instead of requiring the
    // identity database to be dropped and recreated. The retrying execution strategy configured on
    // the DbContext lets this survive Postgres still starting up on first boot.
    await dbContext.Database.MigrateAsync();
}
