using System.Diagnostics;

namespace FoodDeliveryService.LoadTest.Seeder;

/// <summary>
/// Feature 3.5, Milestone B — the deterministic seed fixture.
/// <para>
/// One command produces a known dataset and the `fixtures/seed.json` the k6 scenarios read, so a
/// load run is reproducible and its failures mean something. Everything is created through the
/// public API for a reason spelled out in `PlatformClient`; the single database read is spelled out
/// in `ActivationTokenReader`.
/// </para>
/// </summary>
internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitFailed = 1;
    private const int ExitUsage = 2;

    private static async Task<int> Main(string[] args)
    {
        if (SeederOptions.WantsHelp(args))
        {
            Console.WriteLine(SeederOptions.Usage);

            return ExitSuccess;
        }

        SeederOptions options;

        try
        {
            options = SeederOptions.Parse(args);
        }
        catch (SeederUsageException exception)
        {
            Log.Error(exception.Message);
            await Console.Error.WriteLineAsync();
            await Console.Error.WriteLineAsync(SeederOptions.Usage);

            return ExitUsage;
        }

        using var cancellation = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            return options.Verify
                ? await VerifyAsync(options, cancellation.Token)
                : await SeedAsync(options, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Log.Error("canceled — the fixture was not written, but whatever was already seeded stays " +
                      "and a re-run will pick up from there.");

            return ExitFailed;
        }
        catch (SeederException exception)
        {
            Log.Error(exception.Message);

            return ExitFailed;
        }
        catch (HttpRequestException exception)
        {
            Log.Error($"{exception.Message} (gateway {options.GatewayUrl}, identity {options.IdentityUrl}). " +
                      "Is the stack up, and are these the right addresses for where this tool is running?");

            return ExitFailed;
        }
    }

    private static async Task<int> SeedAsync(SeederOptions options, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var plan = SeedPlan.Build(options);

        using var client = new PlatformClient(options);
        var tokenReader = new ActivationTokenReader(options);
        var activator = new InvitedAccountActivator(client, tokenReader, options);

        Log.Step($"run '{options.RunId}' · gateway {options.GatewayUrl} · identity {options.IdentityUrl} · " +
                 $"environment '{options.Environment}' · seed {options.RandomSeed}");

        // Both preflights before any writing: a wrong admin password or an unreachable database
        // discovered after 500 registrations is 500 registrations of wasted time.
        await tokenReader.EnsureReachableAsync(cancellationToken);

        string adminToken = await client.GetTokenAsync(options.AdminEmail, options.AdminPassword, cancellationToken);

        IReadOnlyList<FixtureRestaurant> restaurants =
            await new CatalogSeeder(client, activator, options).SeedAsync(plan, adminToken, cancellationToken);

        IReadOnlyList<FixtureDriver> drivers =
            await new DriverSeeder(client, activator, options).SeedAsync(plan, adminToken, cancellationToken);

        IReadOnlyList<FixtureCustomer> customers =
            await new CustomerSeeder(client, options).SeedAsync(plan, cancellationToken);

        await new ReplicaProbe(client, options).RunAsync(restaurants, options.ReplicaTimeout, cancellationToken);

        var fixture = new SeedFixture(
            options.RunId,
            DateTime.UtcNow,
            options.Environment,
            options.Prefix,
            restaurants,
            customers,
            drivers);

        fixture.Save(options.OutputPath);

        Log.Done($"wrote {options.OutputPath} in {stopwatch.Elapsed.TotalSeconds:0} s — " +
                 $"{restaurants.Count} restaurants, {customers.Count} customers, {drivers.Count} drivers");

        return ExitSuccess;
    }

    private static async Task<int> VerifyAsync(SeederOptions options, CancellationToken cancellationToken)
    {
        var fixture = SeedFixture.Load(options.OutputPath);

        // The fixture knows which prefix it was seeded with, and the caller of --verify usually does
        // not repeat it. Taking it from the file keeps the verification's probe customer in the same
        // namespace as the data it is checking, instead of quietly creating a `loadtest-` account
        // while verifying a `loadtest-nightly-` fixture.
        options = options with { Prefix = fixture.Prefix };

        using var client = new PlatformClient(options);

        bool verified = await new FixtureVerifier(client, options).VerifyAsync(fixture, cancellationToken);

        return verified ? ExitSuccess : ExitFailed;
    }
}
