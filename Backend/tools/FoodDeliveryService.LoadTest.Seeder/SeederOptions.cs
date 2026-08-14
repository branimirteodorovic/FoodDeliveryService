using System.Globalization;

namespace FoodDeliveryService.LoadTest.Seeder;

/// <summary>
/// Everything the seeder needs, from the command line or the environment. Nothing about a specific
/// stack is compiled in: the compose admin password is a default, not a constant, because the KinD
/// cluster applies ASP.NET Identity's real password rules and uses a different one
/// (KUBERNETES_PHASE2_PLAN.md §2).
/// </summary>
internal sealed record SeederOptions
{
    /// <summary>
    /// Prefix on every identifier this tool creates — emails, restaurant names, tax ids. It is what
    /// makes "delete everything the load test made" a possible sentence, and what lets a re-run
    /// recognise its own data instead of doubling the catalogue.
    /// </summary>
    public string Prefix { get; init; } = "loadtest";

    public string GatewayUrl { get; init; } = "http://localhost:3000";

    public string IdentityUrl { get; init; } = "http://localhost:18080";

    /// <summary>
    /// The Users database, read for one thing only: the `UserInvitedDomainEvent` row carrying an
    /// invited driver's activation token.
    /// </summary>
    // The compose stack's local-development credentials, already in docker-compose.yml and every
    // host's appsettings.Development.json. They are defaults, overridable by --users-connection /
    // --admin-password, and nothing here is a secret for any environment that has real ones.
#pragma warning disable S2068 // Hard-coded credentials are security-sensitive
    public string UsersConnectionString { get; init; } =
        "Host=localhost;Port=5432;Database=fooddeliveryservice_users;Username=postgres;Password=postgres";

    public string AdminEmail { get; init; } = "admin@fooddeliveryservice.com";

    public string AdminPassword { get; init; } = "admin";

    /// <summary>
    /// The password every seeded account gets. Long enough to satisfy ASP.NET Identity's real
    /// rules, so the same fixture seeds compose and KinD.
    /// </summary>
    public string SeededPassword { get; init; } = "Loadtest!23456";
#pragma warning restore S2068

    public int Restaurants { get; init; } = 20;

    public int CategoriesPerRestaurant { get; init; } = 3;

    public int ItemsPerCategory { get; init; } = 8;

    public int Drivers { get; init; } = 50;

    public int Customers { get; init; } = 500;

    /// <summary>
    /// How many registrations/onboardings are in flight at once. Every customer registration is a
    /// PBKDF2 hash on Identity's CPU; unbounded parallelism here turns seeding into an accidental
    /// load test of the one service the harness works hardest to keep out of the measurement.
    /// </summary>
    public int Parallelism { get; init; } = 8;

    /// <summary>Bogus seed. Two runs with the same value produce the same catalogue.</summary>
    public int RandomSeed { get; init; } = 20_260_810;

    /// <summary>
    /// Where the seeded world sits. Driver assignment is a 5 km radius search around the restaurant
    /// (<c>DeliveryAssignmentOptions.SearchRadiusKm</c>), so restaurants and drivers have to share a
    /// city or every order records <c>delivery_assignment_outcome{outcome="no_driver"}</c>.
    /// </summary>
    public double CenterLatitude { get; init; } = 44.7866;

    public double CenterLongitude { get; init; } = 20.4489;

    /// <summary>Restaurants are scattered inside this radius, drivers inside half of it.</summary>
    public double SpreadKm { get; init; } = 2.0;

    /// <summary>
    /// The environment name written into the fixture and checked by <c>lib/fixtures.js</c>. It names
    /// the *stack* (which database the ids belong to), not the URL mode — `compose` and
    /// `compose-host` address the same database from two places.
    /// </summary>
    public string Environment { get; init; } = "compose";

    public string OutputPath { get; init; } = string.Empty;

    /// <summary>
    /// How long to wait for the replicas the order path needs. The outbox ticks every 5 s in
    /// batches of 20 per module, so 500 customers is minutes of propagation, not seconds.
    /// </summary>
    public TimeSpan ReplicaTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Re-read an existing fixture and assert every id still resolves. Seeds nothing.</summary>
    public bool Verify { get; init; }

    public string RunId { get; init; } = string.Empty;

    public static string Usage =>
        """
        Seeds a deterministic load-test dataset through the public API (Feature 3.5, Milestone B).

          dotnet run --project tools/FoodDeliveryService.LoadTest.Seeder [options]

        Options (every one also reads LOADTEST_<UPPER_SNAKE_NAME> from the environment):
          --gateway <url>              default http://localhost:3000
          --identity <url>             default http://localhost:18080
          --users-connection <cs>      Users database, read for driver activation tokens
          --admin-email <email>        default admin@fooddeliveryservice.com
          --admin-password <pw>        default 'admin' (compose); KinD seeds a different one
          --seeded-password <pw>       password given to every seeded account
          --prefix <text>              default 'loadtest' — identifies this tool's data
          --restaurants <n>            default 20
          --categories-per-restaurant <n>  default 3
          --items-per-category <n>     default 8
          --drivers <n>                default 50
          --customers <n>              default 500
          --parallelism <n>            default 8
          --random-seed <n>            Bogus seed; same value => same catalogue
          --center <lat,lng>           default 44.7866,20.4489
          --spread-km <km>             default 2.0 (drivers use half of it)
          --environment <name>         compose | compose-host | kind — written into the fixture
          --output <path>              default <repo>/Backend/loadtest/fixtures/seed.json
          --replica-timeout <seconds>  default 600
          --run-id <text>              default <prefix>-<utc timestamp>
          --verify                     re-check an existing fixture instead of seeding
          --help
        """;

    public static bool WantsHelp(string[] args) =>
        args.Any(a => a is "--help" or "-h" or "-?" or "/?");

    public static SeederOptions Parse(string[] args)
    {
        Dictionary<string, string> values = ReadArguments(args);

        var options = new SeederOptions();

        options = options with
        {
            Prefix = Text(values, "prefix", options.Prefix),
            GatewayUrl = Url(values, "gateway", options.GatewayUrl),
            IdentityUrl = Url(values, "identity", options.IdentityUrl),
            UsersConnectionString = Text(values, "users-connection", options.UsersConnectionString),
            AdminEmail = Text(values, "admin-email", options.AdminEmail),
            AdminPassword = Text(values, "admin-password", options.AdminPassword),
            SeededPassword = Text(values, "seeded-password", options.SeededPassword),
            Restaurants = Count(values, "restaurants", options.Restaurants),
            CategoriesPerRestaurant = Count(values, "categories-per-restaurant", options.CategoriesPerRestaurant),
            ItemsPerCategory = Count(values, "items-per-category", options.ItemsPerCategory),
            Drivers = Count(values, "drivers", options.Drivers),
            Customers = Count(values, "customers", options.Customers),
            Parallelism = Count(values, "parallelism", options.Parallelism),
            RandomSeed = Count(values, "random-seed", options.RandomSeed),
            SpreadKm = Number(values, "spread-km", options.SpreadKm),
            Environment = Text(values, "environment", options.Environment),
            OutputPath = Text(values, "output", options.OutputPath),
            ReplicaTimeout = TimeSpan.FromSeconds(Count(values, "replica-timeout", (int)options.ReplicaTimeout.TotalSeconds)),
            Verify = values.ContainsKey("verify"),
        };

        (double latitude, double longitude) = Center(values, options.CenterLatitude, options.CenterLongitude);

        options = options with
        {
            CenterLatitude = latitude,
            CenterLongitude = longitude,
            RunId = Text(values, "run-id", $"{options.Prefix}-{DateTime.UtcNow:yyyyMMdd-HHmmss}"),
            OutputPath = options.OutputPath.Length > 0 ? options.OutputPath : DefaultOutputPath(),
        };

        if (options.Restaurants == 0)
        {
            throw new SeederUsageException("--restaurants must be at least 1: every other actor is seeded around them.");
        }

        return options;
    }

    /// <summary>
    /// `Backend/loadtest/fixtures/seed.json`, found by walking up from the binary — so the tool
    /// works the same whether it was started from the repository root, from `Backend/`, or from a
    /// published folder next to the solution.
    /// </summary>
    private static string DefaultOutputPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "loadtest", "fixtures");

            if (Directory.Exists(candidate))
            {
                return Path.Combine(candidate, "seed.json");
            }

            directory = directory.Parent;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "seed.json");
    }

    private static Dictionary<string, string> ReadArguments(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        int index = 0;

        while (index < args.Length)
        {
            string argument = args[index++];

            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                throw new SeederUsageException($"unexpected argument '{argument}'.");
            }

            string name = argument[2..];

            int separator = name.IndexOf('=', StringComparison.Ordinal);

            if (separator >= 0)
            {
                values[name[..separator]] = name[(separator + 1)..];

                continue;
            }

            // A flag (--verify) has no value; anything else takes the next argument.
            if (index < args.Length && !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                values[name] = args[index++];

                continue;
            }

            values[name] = string.Empty;
        }

        return values;
    }

    /// <summary>
    /// Command line first, then `LOADTEST_ADMIN_PASSWORD`-style environment variables.
    /// <para>
    /// The prefix is not decoration, and the harness learned it the hard way in Milestone A: bare
    /// names like `USERNAME` are already set on every Windows machine, and silently picking one up
    /// produces a failure that looks like a platform fault.
    /// </para>
    /// </summary>
    private static string? Raw(Dictionary<string, string> values, string name)
    {
        if (values.TryGetValue(name, out string? value) && value.Length > 0)
        {
            return value;
        }

        string variable = $"LOADTEST_{name.Replace('-', '_').ToUpperInvariant()}";

        string? fromEnvironment = System.Environment.GetEnvironmentVariable(variable);

        return string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment;
    }

    private static string Text(Dictionary<string, string> values, string name, string fallback) =>
        Raw(values, name) ?? fallback;

    private static string Url(Dictionary<string, string> values, string name, string fallback) =>
        Text(values, name, fallback).TrimEnd('/');

    private static int Count(Dictionary<string, string> values, string name, int fallback)
    {
        string? raw = Raw(values, name);

        if (raw is null)
        {
            return fallback;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < 0)
        {
            throw new SeederUsageException($"--{name} must be a non-negative whole number (got '{raw}').");
        }

        return parsed;
    }

    private static double Number(Dictionary<string, string> values, string name, double fallback)
    {
        string? raw = Raw(values, name);

        if (raw is null)
        {
            return fallback;
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) || parsed <= 0)
        {
            throw new SeederUsageException($"--{name} must be a positive number (got '{raw}').");
        }

        return parsed;
    }

    private static (double Latitude, double Longitude) Center(
        Dictionary<string, string> values,
        double fallbackLatitude,
        double fallbackLongitude)
    {
        string? raw = Raw(values, "center");

        if (raw is null)
        {
            return (fallbackLatitude, fallbackLongitude);
        }

        string[] parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2 ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude))
        {
            throw new SeederUsageException($"--center must be '<lat>,<lng>' (got '{raw}').");
        }

        return (latitude, longitude);
    }
}
