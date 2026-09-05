using Duende.IdentityServer;
using Duende.IdentityServer.EntityFramework.DbContexts;
using FoodDeliveryService.Identity;
using FoodDeliveryService.Identity.Data;
using FoodDeliveryService.Identity.Seed;
using FoodDeliveryService.Common.Presentation.Correlation;
using FoodDeliveryService.Common.Presentation.Health;
using FoodDeliveryService.Common.Presentation.Security;
using FoodDeliveryService.Common.Presentation.Telemetry;
using FoodDeliveryService.Identity.OpenTelemetry;
using FoodDeliveryService.Identity.Users;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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

// Security response headers on every response, and no `Server: Kestrel` on any of them — Feature
// 3.7 Milestone D. The Add half exists separately from app.UseSecurityHeaders() below for one
// reason: KestrelServerOptions.AddServerHeader is read when the server starts and cannot be set from
// the pipeline.
builder.Services.AddSecurityHeaders(builder.Configuration);

// Feature 3.7 Milestone E — configuration fail-fast. appsettings.json ships every credential blank
// so that a real environment has to supply its own (docs/security.md §3); until now nothing checked
// that it did. A deployment that forgot the confidential client secret booted perfectly happily and
// failed hours later as a 401 from api/users during someone's registration, with nothing in the logs
// pointing at configuration. These keys are validated below, immediately after Build() — see the
// IStartupValidator call — so the host dies at boot naming the key it is missing.
builder.Services.AddRequiredConfiguration(
    builder.Configuration,
    builder.Environment,
    "IdentityServer:IssuerUri",
    "Clients:Confidential:ClientId",
    "Clients:Confidential:ClientSecret",
    "Clients:Public:ClientId");

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
            // Twelve characters on top of ASP.NET Identity's default character-class rules (digit,
            // lower, upper, non-alphanumeric), which are deliberately left alone — the old value of
            // 8 was the only line here and it weakened nothing else, so raising it is the whole
            // change. NOTE: the seeded administrator password must clear this too; raising it to 12
            // is what forced deploy/k8s/base/config.yaml's AdminSeed__Password to grow a character.
            options.Password.RequiredLength = 12;
        }

        // Lockout, in EVERY environment including Development. Without it POST /connect/token is an
        // unrated password oracle: the Gateway's edge rate limiter partitions anonymous callers by
        // IP, which slows one source down and does nothing whatsoever about a distributed attempt,
        // and token issuance does not even pass through the Gateway (clients reach Identity
        // directly, docs/security.md §6.3). Duende's ResourceOwnerPasswordValidator calls
        // SignInManager.CheckPasswordSignInAsync(..., lockoutOnFailure: true), so the counter is
        // driven by the token endpoint itself — nothing extra to wire up.
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// The ASP.NET Data Protection key ring, in the identity database rather than in a directory under
// the content root — Feature 3.7 Milestone E. Two things ride on it and both are replica-local
// without this: Duende encrypts the signing keys it persists with this ring, and the invitation
// activation tokens below are data-protection payloads, so a restart used to invalidate every
// outstanding invitation link. SetApplicationName pins the purpose string, which otherwise defaults
// to the content root path and so differs between a container and a `dotnet run`.
builder.Services
    .AddDataProtection()
    .SetApplicationName("FoodDeliveryService.Identity")
    .PersistKeysToDbContext<ApplicationDbContext>();

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
    .AddAspNetIdentity<ApplicationUser>()
    // Feature 3.7 Milestone E — the operational store, and the reason it exists here.
    //
    // Duende 8 enables automatic key management by default and needs an ISigningKeyStore to keep
    // the keys in. With no operational store registered it falls back to a FileSystemKeyStore
    // writing ./keys under the working directory: per-container, wiped on restart, and NOT shared
    // between replicas. The failure mode is the nasty kind — two pods each advertise their own JWKS,
    // the load balancer sends a token minted by one to a validator that fetched the other's
    // document, and every service rejects perfectly good tokens intermittently. A single-pod restart
    // is the same bug in slow motion: every token issued before it becomes invalid.
    //
    // The same store also holds persisted grants, which matters because the public client sets
    // AllowOfflineAccess — refresh tokens were living in Duende's in-memory grant store and dying
    // with the process. EnableTokenCleanup sweeps the expired ones so the table does not grow
    // without bound, and RemoveConsumedTokens drops one-time refresh tokens once rotated.
    //
    // It connects as the least-privilege fds_identity_app account (ConnectionStrings:Database):
    // key management and grant storage are DML, and 01-roles.sql's ALTER DEFAULT PRIVILEGES already
    // grants the app role rights over whatever the owner's migrations create. The schema itself is
    // migrated by the owner credential in ApplyDatabaseMigrationsAsync below. docs/security.md §4.
    .AddOperationalStore(options =>
    {
        options.ConfigureDbContext = optionsBuilder => optionsBuilder.UseNpgsql(
            databaseConnectionString,
            npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure();
                npgsqlOptions.MigrationsAssembly(typeof(Config).Assembly.FullName);
            });

        options.EnableTokenCleanup = true;
        options.RemoveConsumedTokens = true;
    });

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

// Feature 3.7 Milestone E — run the AddRequiredConfiguration checks HERE rather than leaving them to
// the hosted service ValidateOnStart() registers. That service runs inside app.RunAsync(), which is
// after the two lines below: a host missing its client secret would migrate a schema and seed an
// administrator before telling anyone. This is the same validator, pulled one step earlier so that
// "fail fast" means before any side effect.
app.Services.GetRequiredService<IStartupValidator>().Validate();

await ApplyDatabaseMigrationsAsync(app);

// Config-driven initial-administrator seed (idempotent; no-ops when "AdminSeed" is empty).
await AdminSeeder.SeedAdminAsync(app);

// One shared middleware for all nine hosts (Common.Presentation/Security): nosniff, DENY framing,
// no referrer, a `default-src 'none'` CSP for the JSON surface, and HSTS only when the request
// actually arrived over HTTPS. It is placed first so that a response short-circuited downstream — an
// authentication challenge, a rate-limit rejection, the exception handler — is stamped too.
app.UseSecurityHeaders();

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
    // Feature 3.7 Milestone C: the context is built here rather than resolved from DI, because the
    // registered one is bound to ConnectionStrings:Database — the least-privilege fds_identity_app
    // account, which holds no DDL rights. Migrations are the one code path allowed the
    // fds_identity_owner credential, and it never reaches a request-serving pool. The module hosts
    // do the same thing through the shared DatabaseMigrationExtensions.ApplyMigration<T>(); Identity
    // takes no Common.Infrastructure dependency, so it repeats the four lines instead of the
    // reference. docs/security.md §4.
    string migrationsConnectionString =
        app.Configuration.GetConnectionString("DatabaseMigrations") ??
        app.Configuration.GetConnectionString("Database") ??
        throw new InvalidOperationException(
            "Neither the 'DatabaseMigrations' nor the 'Database' connection string is configured.");

    DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(migrationsConnectionString, npgsqlOptions => npgsqlOptions.EnableRetryOnFailure())
        .Options;

    await using var dbContext = new ApplicationDbContext(options);

    // Applies EF Core migrations (Data/Migrations) at startup — mirrors the modules'
    // app.ApplyMigrations(). Unlike EnsureCreated this evolves the schema, so changes to
    // ApplicationUser (e.g. MustChangePassword) ship as new migrations instead of requiring the
    // identity database to be dropped and recreated. The retrying execution strategy configured
    // above lets this survive Postgres still starting up on first boot.
    await dbContext.Database.MigrateAsync();

    // Feature 3.7 Milestone E: the operational store's own schema (signing keys, persisted grants,
    // server-side sessions), in the same database and on the same owner credential. It is a second
    // DbContext sharing the one `__EFMigrationsHistory` table; the migration ids do not collide
    // because each context only ever applies its own.
    //
    // UseApplicationServiceProvider is NOT optional here, and its absence is a crash rather than a
    // subtle bug: PersistedGrantDbContext.OnModelCreating resolves OperationalStoreOptions through
    // the context's own service provider, so a hand-constructed context throws "Unable to resolve
    // service for type ... OperationalStoreOptions" — a message that reads like a missing database
    // provider and is not one. EF falls back to the application service provider for services it
    // does not know, which is where AddOperationalStore registered those options. The connection
    // string still comes from UseNpgsql above, so this borrows the options and not the credential.
    DbContextOptions<PersistedGrantDbContext> operationalOptions =
        new DbContextOptionsBuilder<PersistedGrantDbContext>()
            .UseNpgsql(
                migrationsConnectionString,
                npgsqlOptions =>
                {
                    npgsqlOptions.EnableRetryOnFailure();
                    // MigrationsAssembly has to be repeated here. It is set on the DI registration
                    // above, but this is a separate options object, and without it EF looks for
                    // migrations in the assembly that declares PersistedGrantDbContext — Duende's —
                    // finds none, and MigrateAsync succeeds having done nothing at all. The host
                    // then starts healthy with no Keys or PersistedGrants table, which is the exact
                    // state this milestone exists to remove.
                    npgsqlOptions.MigrationsAssembly(typeof(Config).Assembly.FullName);
                })
            .UseApplicationServiceProvider(app.Services)
            .Options;

    await using var operationalDbContext = new PersistedGrantDbContext(operationalOptions);

    await operationalDbContext.Database.MigrateAsync();
}
