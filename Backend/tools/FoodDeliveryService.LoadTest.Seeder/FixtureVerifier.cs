namespace FoodDeliveryService.LoadTest.Seeder;

/// <summary>
/// `--verify`: re-reads an existing fixture and asserts every id in it still resolves through the
/// API, plus that its credentials still work and an order can still be placed.
/// <para>
/// It is the cheap answer to the question that costs the most time to answer any other way — "is
/// this run failing because of the change I made, or because somebody ran `docker compose down -v`
/// since the fixture was written?"
/// </para>
/// </summary>
internal sealed class FixtureVerifier(PlatformClient client, SeederOptions options)
{
    /// <summary>Credentials are expensive to check (PBKDF2), so a sample is enough to catch a re-created database.</summary>
    private const int CredentialSampleSize = 5;

    public async Task<bool> VerifyAsync(SeedFixture fixture, CancellationToken cancellationToken)
    {
        Log.Step($"verifying {options.OutputPath}");
        Log.Info($"  run '{fixture.RunId}' · seeded {fixture.GeneratedOnUtc:u} against '{fixture.Environment}'");

        var problems = new List<string>();

        string adminToken = await client.GetTokenAsync(options.AdminEmail, options.AdminPassword, cancellationToken);

        foreach (FixtureRestaurant restaurant in fixture.Restaurants)
        {
            MenuResponse? menu = null;

            try
            {
                menu = await client.GetMenuAsync(restaurant.RestaurantId, adminToken, cancellationToken);
            }
            catch (SeederException exception)
            {
                problems.Add($"restaurant {restaurant.RestaurantId} ({restaurant.Name}): {exception.Message}");

                continue;
            }

            HashSet<Guid> live = menu is null
                ? []
                : menu.Categories.SelectMany(category => category.Items).Select(item => item.Id).ToHashSet();

            Guid[] missing = restaurant.MenuItemIds.Where(id => !live.Contains(id)).ToArray();

            if (missing.Length > 0)
            {
                problems.Add(
                    $"restaurant {restaurant.RestaurantId} ({restaurant.Name}): " +
                    $"{missing.Length} of {restaurant.MenuItemIds.Count} menu items no longer exist");
            }
        }

        Log.Info($"  {fixture.Restaurants.Count} restaurants and their menus checked");

        await VerifyCredentialsAsync(
            "customer",
            fixture.Customers.Select(customer => (customer.Email, customer.Password)),
            problems,
            cancellationToken);

        await VerifyCredentialsAsync(
            "driver",
            fixture.Drivers.Select(driver => (driver.Email, driver.Password)),
            problems,
            cancellationToken);

        await VerifyCredentialsAsync(
            "manager",
            fixture.Restaurants.Select(restaurant => (restaurant.ManagerEmail, restaurant.ManagerPassword)),
            problems,
            cancellationToken);

        // The end-to-end question: are the replicas still there? One order against one restaurant
        // answers it, and a short timeout is right here — this is a check, not a wait.
        if (fixture.Restaurants.Count > 0 && problems.Count == 0)
        {
            await new ReplicaProbe(client, options).RunAsync(
                [fixture.Restaurants[0]],
                TimeSpan.FromSeconds(30),
                cancellationToken);
        }

        foreach (string problem in problems)
        {
            Log.Error(problem);
        }

        if (problems.Count > 0)
        {
            Log.Error($"fixture is stale — {problems.Count} problem(s). Re-run the seeder.");

            return false;
        }

        Log.Done("fixture verified: every id resolves, credentials work, orders can be placed");

        return true;
    }

    private async Task VerifyCredentialsAsync(
        string role,
        IEnumerable<(string Email, string Password)> accounts,
        List<string> problems,
        CancellationToken cancellationToken)
    {
        (string Email, string Password)[] sample = accounts.Take(CredentialSampleSize).ToArray();

        foreach ((string email, string password) in sample)
        {
            if (await client.TryGetTokenAsync(email, password, cancellationToken) is null)
            {
                problems.Add($"{role} '{email}' can no longer log in");
            }
        }

        if (sample.Length > 0)
        {
            Log.Info($"  {sample.Length} {role} credential(s) sampled");
        }
    }
}
