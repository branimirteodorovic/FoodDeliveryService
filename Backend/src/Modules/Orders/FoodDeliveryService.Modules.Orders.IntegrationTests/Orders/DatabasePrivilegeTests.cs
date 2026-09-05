using System.Diagnostics.CodeAnalysis;
using AwesomeAssertions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace FoodDeliveryService.Modules.Orders.IntegrationTests.Orders;

/// <summary>
/// Feature 3.7 Milestone C, §4.4. The database privilege model against a real PostgreSQL.
/// <para>
/// Every service used to connect as the superuser <c>postgres</c>, which made a SQL-injection or
/// deserialisation bug in any one host a full-platform compromise and left Hard Rule #5 ("never
/// query another service's tables") enforced by convention alone. The two properties below are what
/// replaced that convention, and neither is expressible without a server: only PostgreSQL can say
/// whether <c>fds_orders_app</c> may create a table or open <c>fooddeliveryservice_users</c>.
/// </para>
/// <para>
/// This class stands up its own container rather than reusing
/// <c>IntegrationTestWebAppFactory</c>'s: the whole point is a cluster initialised by the shipped
/// <c>docker/postgres/init/01-roles.sql</c>, and it asserts the file compose bind-mounts and the
/// KinD ConfigMap is generated from — not a copy of it. See <c>docs/security.md</c> §4.
/// </para>
/// </summary>
public sealed class DatabasePrivilegeTests : IAsyncLifetime
{
    private const string OrdersDatabase = "fooddeliveryservice_orders";
    private const string UsersDatabase = "fooddeliveryservice_users";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17")
        .WithDatabase(OrdersDatabase)
        .WithUsername("postgres")
        .WithPassword("postgres")
        // The image runs everything in this directory once, on an empty data directory, before it
        // accepts a connection — the same mechanism compose and the StatefulSet use.
        .WithResourceMapping(new FileInfo(RolesScriptPath()), "/docker-entrypoint-initdb.d/")
        .Build();

    public async ValueTask InitializeAsync() => await _database.StartAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task OwnerRole_CanCreateSchemaObjects()
    {
        // The startup migration path, and the only thing that credential is used for.
        await using NpgsqlConnection connection = await OpenAsync("fds_orders_owner", OrdersDatabase);

        await ExecuteAsync(connection, "CREATE TABLE privilege_probe (id integer PRIMARY KEY)");

        (await ScalarAsync(connection, "SELECT to_regclass('public.privilege_probe') IS NOT NULL"))
            .Should().Be(true);
    }

    [Fact]
    public async Task AppRole_CanReadAndWriteTablesTheOwnerCreatesLater()
    {
        // ALTER DEFAULT PRIVILEGES is the grant under test: the tables do not exist when the init
        // script runs, so a plain "GRANT ... ON ALL TABLES" would leave every future migration's
        // tables unreadable — the failure would surface as a 500 on the first request, not at boot.
        await using (NpgsqlConnection owner = await OpenAsync("fds_orders_owner", OrdersDatabase))
        {
            await ExecuteAsync(owner, "CREATE TABLE deferred_grant_probe (id integer PRIMARY KEY)");
        }

        await using NpgsqlConnection app = await OpenAsync("fds_orders_app", OrdersDatabase);

        await ExecuteAsync(app, "INSERT INTO deferred_grant_probe VALUES (1)");
        (await ScalarAsync(app, "SELECT count(*)::integer FROM deferred_grant_probe")).Should().Be(1);

        await ExecuteAsync(app, "UPDATE deferred_grant_probe SET id = 2");
        await ExecuteAsync(app, "DELETE FROM deferred_grant_probe");
    }

    [Fact]
    public async Task AppRole_CannotCreateSchemaObjects()
    {
        // The connection is opened inside the lambda rather than captured from this scope: a
        // disposable captured by a task the analyzer cannot prove completes first is CA2025.
        Func<Task> createTable = async () =>
        {
            await using NpgsqlConnection connection = await OpenAsync("fds_orders_app", OrdersDatabase);
            await ExecuteAsync(connection, "CREATE TABLE forbidden (id integer)");
        };

        // 42501 = insufficient_privilege. The credential every request-serving pool holds cannot
        // change the schema, so a SQL injection reaching it cannot drop a table or install a
        // trigger — it is bounded by the rows the service already reads and writes.
        (await createTable.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task AppRole_CannotOpenAnotherServicesDatabase()
    {
        Func<Task> connect = () => OpenAsync("fds_orders_app", UsersDatabase);

        // Hard Rule #5, enforced by the server rather than by review. This is what
        // `REVOKE CONNECT ON DATABASE ... FROM PUBLIC` buys: the connection is refused before any
        // query is parsed, so no grant inside the Users database has to be got right for Orders to
        // be unable to read it.
        (await connect.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task EveryServiceDatabase_IsOwnedByItsOwnOwnerRole()
    {
        await using NpgsqlConnection connection = await OpenAsync("postgres", OrdersDatabase, "postgres");

        await using var command = new NpgsqlCommand(
            """
            SELECT datname, pg_get_userbyid(datdba)
            FROM pg_database
            WHERE datname LIKE 'fooddeliveryservice\_%'
            """,
            connection);

        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                owners[reader.GetString(0)] = reader.GetString(1);
            }
        }

        owners.Should().HaveCount(8, "one database per service, created by the init script");

        foreach ((string database, string owner) in owners)
        {
            string service = database["fooddeliveryservice_".Length..];

            // fooddeliveryservice_orders arrives here already created by the container entrypoint
            // (POSTGRES_DB) and owned by `postgres`, so the script's unconditional ALTER DATABASE
            // OWNER is doing real work for this row — not just decorating a CREATE it also ran.
            owner.Should().Be($"fds_{service}_owner");
        }
    }

    private async Task<NpgsqlConnection> OpenAsync(string role, string database, string? password = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(_database.GetConnectionString())
        {
            Database = database,
            Username = role,
            Password = password ?? $"{role}_dev",

            // Npgsql's 15 s default is a machine-load timer, not a privilege one: a full-solution
            // run has every suite starting its own containers at once, and a connect that loses
            // that race throws NpgsqlException/TimeoutException — which reads as a failed privilege
            // assertion in tests whose whole point is WHICH PostgresException comes back. A refusal
            // (42501) is still returned by the server immediately; only the slow success path moves.
            Timeout = 60
        };

        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        return connection;
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Every statement is a literal in this file; there is no caller-supplied input to parameterise.")]
    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Every statement is a literal in this file; there is no caller-supplied input to parameterise.")]
    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Walks up to the <c>Backend/</c> root — the one directory holding both
    /// <c>docker-compose.yml</c> and <c>docker/</c> — the same way <c>Common.UnitTests</c> finds
    /// repository assets.
    /// </summary>
    private static string RolesScriptPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "docker", "postgres", "init", "01-roles.sql");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("docker/postgres/init/01-roles.sql was not found above the test output.");
    }
}
