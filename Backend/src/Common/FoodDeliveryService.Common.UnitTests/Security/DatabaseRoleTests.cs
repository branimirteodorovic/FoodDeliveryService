using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using YamlDotNet.RepresentationModel;

namespace FoodDeliveryService.Common.UnitTests.Security;

/// <summary>
/// Feature 3.7 Milestone C. The database privilege model, asserted from the files that define it.
/// <para>
/// The property this suite protects is not "the SQL is correct" — a real Postgres proves that, in
/// <c>Orders.IntegrationTests/DatabasePrivilegeTests</c>. It is the far easier thing to break: a
/// service pointed at the wrong role. Every credential in this platform lives in a connection
/// string, in three places (a host's development settings, the Kubernetes Secret, and the
/// Deployment that maps it), and none of them fail loudly when they name another service's role —
/// they just quietly restore the cross-service access the roles exist to remove. A superuser
/// reintroduced "to get the migration working" is the same class of regression and equally silent.
/// </para>
/// <para>
/// See <c>docker/postgres/init/01-roles.sql</c> and <c>docs/security.md</c> §4.
/// </para>
/// </summary>
public class DatabaseRoleTests
{
    private const string RolesSqlPath = "docker/postgres/init/01-roles.sql";

    /// <summary>
    /// Host project directory → the service key used for its database, its two roles and its
    /// <c>platform-secrets</c> entries. The Gateway is absent because it owns no database.
    /// </summary>
    private static readonly (string HostDirectory, string Service, string SecretKey)[] Hosts =
    [
        ("FoodDeliveryService.Identity", "identity", "Identity"),
        ("FoodDeliveryService.Users.Api", "users", "Users"),
        ("FoodDeliveryService.Orders.Api", "orders", "Orders"),
        ("FoodDeliveryService.Restaurants.Api", "restaurants", "Restaurants"),
        ("FoodDeliveryService.Notifications.Api", "notifications", "Notifications"),
        ("FoodDeliveryService.Delivery.Api", "delivery", "Delivery"),
        ("FoodDeliveryService.RealTime.Api", "realtime", "RealTime"),
        ("FoodDeliveryService.Support.Api", "support", "Support")
    ];

    public static TheoryData<string, string, string> HostData()
    {
        var data = new TheoryData<string, string, string>();
        foreach ((string hostDirectory, string service, string secretKey) in Hosts)
        {
            data.Add(hostDirectory, service, secretKey);
        }

        return data;
    }

    [Fact]
    public void RolesScript_DefinesBothRolesForEveryServiceDatabase()
    {
        string sql = File.ReadAllText(RepositoryPaths.Backend(RolesSqlPath.Split('/')));

        // The databases the script actually connects to and grants inside — not the array literal it
        // loops over, which would let a database be created and then never granted anything.
        string[] granted = Regex
            .Matches(sql, @"\\connect fooddeliveryservice_(\w+)")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        granted.Should().BeEquivalentTo(
            Hosts.Select(host => host.Service),
            "every host with a database needs its schema privileges granted, and a database no host " +
            "uses is a database nothing should create");

        foreach ((_, string service, _) in Hosts)
        {
            sql.Should().Contain(
                $"GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO fds_{service}_app",
                $"fds_{service}_app must reach tables a LATER migration creates — the tables do not " +
                "exist when this script runs, so ALTER DEFAULT PRIVILEGES is the only grant covering them");

            sql.Should().NotContain(
                $"GRANT CREATE ON SCHEMA public TO fds_{service}_app",
                "the app role holding DDL is exactly the escalation the owner/app split removes");
        }

        sql.Should().Contain(
            "REVOKE CONNECT ON DATABASE",
            "without this every role can open every database and the per-schema grants decide only " +
            "what it can do once inside");
    }

    [Theory]
    [MemberData(nameof(HostData))]
    public void DevelopmentSettings_UseTheServicesOwnRoles(string hostDirectory, string service, string secretKey)
    {
        _ = secretKey;

        string path = RepositoryPaths.Backend("src", "API", hostDirectory, "appsettings.Development.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement connectionStrings = document.RootElement.GetProperty("ConnectionStrings");

        AssertConnection(connectionStrings.GetProperty("Database").GetString()!, service, "app", hostDirectory);
        AssertConnection(
            connectionStrings.GetProperty("DatabaseMigrations").GetString()!, service, "owner", hostDirectory);
    }

    [Theory]
    [MemberData(nameof(HostData))]
    public void PlatformSecrets_UseTheServicesOwnRoles(string hostDirectory, string service, string secretKey)
    {
        _ = hostDirectory;

        Dictionary<string, string> secrets = PlatformSecrets();

        AssertConnection(secrets[$"Database__{secretKey}"], service, "app", secretKey);
        AssertConnection(secrets[$"DatabaseMigrations__{secretKey}"], service, "owner", secretKey);
    }

    [Theory]
    [MemberData(nameof(HostData))]
    public void Deployment_MapsBothCredentialsFromTheMatchingSecretKeys(
        string hostDirectory,
        string service,
        string secretKey)
    {
        _ = hostDirectory;

        string manifest = File.ReadAllText(
            RepositoryPaths.Backend("deploy", "k8s", "services", $"{service}.yaml"));

        manifest.Should().Contain($"key: Database__{secretKey}");
        manifest.Should().Contain($"key: DatabaseMigrations__{secretKey}");
        manifest.Should().Contain(
            "name: ConnectionStrings__DatabaseMigrations",
            $"{service}.yaml must pass the owner credential through, or the pod starts with the app " +
            "role for migrations and fails on the first CREATE TABLE");
    }

    /// <summary>
    /// §4.3 of the plan: the two-credential split adds a second, near-idle pool per host, and the
    /// tuned pool sizes were measured against a real <c>53300: too many clients</c> failure. This
    /// re-derives the arithmetic from the files rather than trusting the comment next to them.
    /// </summary>
    [Fact]
    public void BoundedConnectionTotal_FitsInsideTheServersMaxConnections()
    {
        Dictionary<string, string> secrets = PlatformSecrets();

        int total = 0;
        foreach ((_, string service, string secretKey) in Hosts)
        {
            // A module host builds two pools from the app connection string — the shared
            // NpgsqlDataSource that Dapper and the outbox/inbox jobs use, and EF Core's own.
            // Identity registers only the DbContext, so it builds one.
            int pools = service == "identity" ? 1 : 2;
            total += pools * MaximumPoolSize(secrets[$"Database__{secretKey}"]);

            int migrationPool = MaximumPoolSize(secrets[$"DatabaseMigrations__{secretKey}"]);
            migrationPool.Should().BeLessThanOrEqualTo(
                2,
                $"the {service} migration pool holds the DDL-capable credential and runs one " +
                "sequential migration at boot — anything larger is a privileged pool sitting idle");

            total += migrationPool;
        }

        string postgres = File.ReadAllText(RepositoryPaths.Backend("deploy", "k8s", "base", "postgres.yaml"));
        // The `args:` line, not the first `max_connections=` in the file — the comment above it
        // quotes the image's default of 100, and matching that made this test read the ceiling as
        // 100 when the server is actually started with 200.
        Match maxConnections = Regex.Match(postgres, @"args: \[""-c"", ""max_connections=(\d+)""\]");
        maxConnections.Success.Should().BeTrue("postgres.yaml starts the server with an explicit ceiling");

        int ceiling = int.Parse(maxConnections.Groups[1].Value, CultureInfo.InvariantCulture);

        // Headroom, not a rounding allowance: psql, the load-test seeder and a one-off job pod all
        // need to get in while every host is at its limit.
        total.Should().BeLessThan(
            ceiling - 20,
            $"the bounded worst case is {total} connections against a server ceiling of {ceiling}");
    }

    [Fact]
    public void NoDeployedConnectionString_ConnectsAsTheSuperuser()
    {
        List<string> offenders = [];

        foreach ((string hostDirectory, _, _) in Hosts)
        {
            string path = RepositoryPaths.Backend("src", "API", hostDirectory, "appsettings.Development.json");
            if (File.ReadAllText(path).Contains("Username=postgres", StringComparison.Ordinal))
            {
                offenders.Add(hostDirectory);
            }
        }

        offenders.AddRange(PlatformSecrets()
            .Where(entry => entry.Value.Contains("Username=postgres", StringComparison.Ordinal))
            .Select(entry => $"platform-secrets/{entry.Key}"));

        offenders.Should().BeEmpty(
            "connecting as the Postgres superuser makes a bug in any one service a full-platform " +
            "compromise — which is the status quo Milestone C replaced");
    }

    private static void AssertConnection(string connectionString, string service, string role, string origin)
    {
        connectionString.Should().Contain(
            $"Database=fooddeliveryservice_{service};", $"{origin} owns that database");
        connectionString.Should().Contain(
            $"Username=fds_{service}_{role};",
            $"{origin} must use its own {role} role — naming another service's role restores exactly " +
            "the cross-database access Hard Rule #5 assumes is impossible");
        connectionString.Should().Contain(
            $"Password=fds_{service}_{role}_dev",
            "the password must match what 01-roles.sql creates, and must differ per role: two roles " +
            "sharing one password means a leaked app credential also opens the owner account");
    }

    private static int MaximumPoolSize(string connectionString)
    {
        Match match = Regex.Match(connectionString, @"Maximum Pool Size=(\d+)");
        match.Success.Should().BeTrue($"'{connectionString}' must bound its pool explicitly");

        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, string> PlatformSecrets()
    {
        using var reader = new StreamReader(RepositoryPaths.Backend("deploy", "k8s", "base", "config.yaml"));
        var yaml = new YamlStream();
        yaml.Load(reader);

        YamlMappingNode secret = yaml.Documents
            .Select(document => (YamlMappingNode)document.RootNode)
            .Single(node => ((YamlScalarNode)node["kind"]).Value == "Secret");

        return ((YamlMappingNode)secret["stringData"]).Children
            .ToDictionary(
                entry => ((YamlScalarNode)entry.Key).Value!,
                entry => ((YamlScalarNode)entry.Value).Value!);
    }
}
