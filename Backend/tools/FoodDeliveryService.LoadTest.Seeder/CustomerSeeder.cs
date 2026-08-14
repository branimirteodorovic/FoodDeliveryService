using System.Diagnostics;

namespace FoodDeliveryService.LoadTest.Seeder;

/// <summary>
/// Customers, through the one anonymous endpoint the platform has.
/// <para>
/// This is the slowest step of the seeder by a wide margin and it is worth knowing why: every
/// registration is an ASP.NET Identity PBKDF2 hash, which burns CPU deliberately. Five hundred of
/// them is minutes of Identity's time, so it runs with bounded parallelism and says how far along it
/// is — and it is also the reason `lib/auth.js` logs in once per VU rather than once per iteration.
/// </para>
/// </summary>
internal sealed class CustomerSeeder(PlatformClient client, SeederOptions options)
{
    public async Task<IReadOnlyList<FixtureCustomer>> SeedAsync(SeedPlan plan, CancellationToken cancellationToken)
    {
        if (plan.Customers.Count == 0)
        {
            return [];
        }

        Log.Step($"customers: {plan.Customers.Count} (PBKDF2 per registration — this is the slow step), " +
                 $"{options.Parallelism} at a time");

        var seeded = new FixtureCustomer[plan.Customers.Count];
        var stopwatch = Stopwatch.StartNew();
        int registered = 0;
        int reused = 0;
        int completed = 0;

        await Parallel.ForEachAsync(
            plan.Customers,
            new ParallelOptions { MaxDegreeOfParallelism = options.Parallelism, CancellationToken = cancellationToken },
            async (spec, token) =>
            {
                ApiResult<Guid> result = await client.TryRegisterCustomerAsync(spec, options.SeededPassword, token);

                if (result.IsSuccess)
                {
                    Interlocked.Increment(ref registered);
                }
                else
                {
                    // Registration refuses a duplicate email, which on a re-run is the expected
                    // answer. Prove it is that and not something else by logging in as them.
                    string? existing = await client.TryGetTokenAsync(spec.Email, options.SeededPassword, token);

                    if (existing is null)
                    {
                        throw new SeederException(
                            $"registering '{spec.Email}' failed ({result.Detail}) and the account cannot log " +
                            "in with the seeded password either. Re-seed a clean stack " +
                            "(`docker compose down -v`) or pick another --prefix.");
                    }

                    Interlocked.Increment(ref reused);
                }

                seeded[spec.Index] = new FixtureCustomer(spec.Email, options.SeededPassword);

                int done = Interlocked.Increment(ref completed);

                if (done % 100 == 0 || done == plan.Customers.Count)
                {
                    Log.Info($"  {done}/{plan.Customers.Count} customers ({stopwatch.Elapsed.TotalSeconds:0} s)");
                }
            });

        Log.Done($"customers ready: {seeded.Length} ({registered} new, {reused} already existed) " +
                 $"in {stopwatch.Elapsed.TotalSeconds:0} s");

        return seeded;
    }
}
