using System.Diagnostics;

namespace FoodDeliveryService.LoadTest.Seeder;

/// <summary>
/// The step that separates a working fixture from a load test that reports a 100% error rate for
/// reasons nobody can find.
/// <para>
/// `POST orders` needs two things to have arrived in the *Orders* database, neither of which the
/// endpoints above wrote there: the customer (Users → `UserRegistered` → Orders inbox) and the
/// restaurant with its menu items (Restaurants → Orders replica). Both travel through the outbox,
/// which ticks every 5 s in batches of 20 per module — so a 500-customer seed propagates for
/// minutes after the last HTTP call returned 200. A fixture written at that moment is a fixture full
/// of ids the order path cannot resolve yet.
/// </para>
/// <para>
/// So the seeder does not declare success until a real order has been placed against every seeded
/// restaurant, using a throwaway customer registered *after* all the others. That ordering is the
/// whole trick: `ProcessOutboxJob` selects `ORDER BY occurred_on_utc`, so the probe customer's
/// replica arriving means every registration queued ahead of it has already been dispatched.
/// </para>
/// </summary>
internal sealed class ReplicaProbe(PlatformClient client, SeederOptions options)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    public async Task RunAsync(
        IReadOnlyList<FixtureRestaurant> restaurants,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        CustomerSpec probeSpec = SeedPlan.ProbeCustomer(options);

        // Registered last, on purpose — see the class comment.
        ApiResult<Guid> registration = await client.TryRegisterCustomerAsync(
            probeSpec,
            options.SeededPassword,
            cancellationToken);

        if (!registration.IsSuccess)
        {
            Log.Info($"  probe customer already existed ({probeSpec.Email})");
        }

        string probeToken = await client.GetTokenAsync(probeSpec.Email, options.SeededPassword, cancellationToken);

        Log.Step($"waiting for the Orders replicas (probe order per restaurant, up to {timeout.TotalMinutes:0} min)");

        var stopwatch = Stopwatch.StartNew();
        DateTime deadline = DateTime.UtcNow + timeout;
        bool first = true;

        foreach (FixtureRestaurant restaurant in restaurants)
        {
            string lastDetail = await ProbeAsync(restaurant, probeToken, deadline, cancellationToken);

            if (lastDetail.Length > 0)
            {
                throw new SeederException(
                    $"no order could be placed against '{restaurant.Name}' within " +
                    $"{timeout.TotalMinutes:0} min. Last answer: {lastDetail}\n" +
                    "This is almost always outbox lag, not a broken fixture: the customer and the " +
                    "restaurant/menu replicas reach Orders through jobs configured at " +
                    "IntervalInSeconds 5 / BatchSize 20 per module (MessageProcessor in each host's " +
                    "appsettings.Development.json), so a large seed takes minutes to propagate. " +
                    "Check the outbox backlog and the Orders inbox before blaming the seeder, or " +
                    "raise --replica-timeout.");
            }

            if (first)
            {
                Log.Info($"  first order accepted after {stopwatch.Elapsed.TotalSeconds:0} s — the pipeline is flowing");
                first = false;
            }
        }

        Log.Done($"replicas confirmed for {restaurants.Count} restaurant(s) in {stopwatch.Elapsed.TotalSeconds:0} s");
    }

    /// <summary>
    /// Polls one restaurant until an order is accepted. Returns an empty string on success, or the
    /// last failure detail if the deadline passed.
    /// </summary>
    private async Task<string> ProbeAsync(
        FixtureRestaurant restaurant,
        string probeToken,
        DateTime deadline,
        CancellationToken cancellationToken)
    {
        if (restaurant.MenuItemIds.Count == 0)
        {
            return $"'{restaurant.Name}' has no menu items in the fixture";
        }

        string detail = "never attempted";

        while (DateTime.UtcNow < deadline)
        {
            // A fresh key every attempt. Reusing one would make the second attempt return the first
            // attempt's order id from the idempotency lookup instead of re-testing the replica —
            // the probe would pass the moment it had ever passed, which is not what it is for.
            ApiResult<Guid> result = await client.TryPlaceOrderAsync(
                restaurant,
                [restaurant.MenuItemIds[0]],
                probeToken,
                $"{options.RunId}-probe-{Guid.NewGuid():N}",
                cancellationToken);

            if (result.IsSuccess)
            {
                return string.Empty;
            }

            detail = result.Detail;

            await Task.Delay(PollInterval, cancellationToken);
        }

        return detail;
    }
}
