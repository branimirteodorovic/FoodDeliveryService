namespace FoodDeliveryService.LoadTest.Seeder;

/// <summary>
/// Drivers: onboard → activate the invitation → clock on → report a position.
/// <para>
/// The last two steps are the ones that matter and the ones easiest to leave out. Assignment
/// searches Redis for available drivers within 5 km of the restaurant, and a driver who has never
/// posted a location is not in that GEO set at all — so a fixture of 50 perfectly good driver
/// accounts still produces a run where every single order records
/// <c>delivery_assignment_outcome{outcome="no_driver"}</c> and the entire delivery half of the
/// platform goes unmeasured.
/// </para>
/// </summary>
internal sealed class DriverSeeder(
    PlatformClient client,
    InvitedAccountActivator activator,
    SeederOptions options)
{
    public async Task<IReadOnlyList<FixtureDriver>> SeedAsync(
        SeedPlan plan,
        string adminToken,
        CancellationToken cancellationToken)
    {
        if (plan.Drivers.Count == 0)
        {
            return [];
        }

        Log.Step($"drivers: {plan.Drivers.Count}, positioned within " +
                 $"{options.SpreadKm / 2:0.##} km of the centre");

        var seeded = new FixtureDriver[plan.Drivers.Count];
        int reused = 0;

        await Parallel.ForEachAsync(
            plan.Drivers,
            new ParallelOptions { MaxDegreeOfParallelism = options.Parallelism, CancellationToken = cancellationToken },
            async (spec, token) =>
            {
                string? driverToken = await client.TryGetTokenAsync(spec.Email, options.SeededPassword, token);
                Guid? driverId = null;

                if (driverToken is null)
                {
                    ApiResult<Guid> onboarded = await client.TryOnboardDriverAsync(spec, adminToken, token);

                    // A failure here is not fatal on its own: an interrupted earlier run may have
                    // onboarded this driver already, in which case the account exists and the
                    // activation below is exactly what is missing. If it was something else, the
                    // activator raises the real error.
                    if (onboarded.IsSuccess)
                    {
                        driverId = onboarded.Value;
                    }

                    driverToken = await activator.ActivateAsync(spec.Email, token);
                }
                else
                {
                    Interlocked.Increment(ref reused);
                }

                driverId ??= await client.GetMyDriverIdAsync(driverToken, token);

                // Order matters: availability first, position second. A location report from an
                // Offline driver is refused outright (DriverErrors.Offline), and the pool is only
                // entered by a driver who is Available.
                await client.SetAvailabilityAsync(true, driverToken, token);
                await client.RecordLocationAsync(spec.Latitude, spec.Longitude, driverToken, token);

                seeded[spec.Index] = new FixtureDriver(driverId.Value, spec.Email, options.SeededPassword);
            });

        Log.Done($"drivers ready: {seeded.Length} ({reused} already existed), all available and on the map");

        return seeded;
    }
}
