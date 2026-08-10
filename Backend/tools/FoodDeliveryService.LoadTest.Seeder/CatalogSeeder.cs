namespace FoodDeliveryService.LoadTest.Seeder;

/// <summary>
/// Restaurants, their menus, and the manager account onboarding provisions along with them.
/// <para>
/// Menus are created by the administrator rather than by each manager: `RestaurantOwnership` lets an
/// admin modify any restaurant, and doing it as the manager would mean activating 20 accounts before
/// the first menu item exists. The managers are activated anyway (Milestone C needs them to drive
/// the order lifecycle), just not on the critical path.
/// </para>
/// </summary>
internal sealed class CatalogSeeder(
    PlatformClient client,
    InvitedAccountActivator activator,
    SeederOptions options)
{
    public async Task<IReadOnlyList<FixtureRestaurant>> SeedAsync(
        SeedPlan plan,
        string adminToken,
        CancellationToken cancellationToken)
    {
        Log.Step($"restaurants: {plan.Restaurants.Count} × {options.CategoriesPerRestaurant} categories × " +
                 $"{options.ItemsPerCategory} items");

        // One listing, matched on the tax identification this tool stamps. Everything about
        // re-running safely hangs off it: without this the second run onboards a second catalogue
        // and every later measurement is against a database nobody meant to create.
        IReadOnlyList<RestaurantSummary> existing = await client.GetAllRestaurantsAsync(adminToken, cancellationToken);

        var existingByTaxId = existing
            .Where(restaurant => restaurant.TaxIdentification.StartsWith(
                options.Prefix,
                StringComparison.OrdinalIgnoreCase))
            .GroupBy(restaurant => restaurant.TaxIdentification, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var seeded = new FixtureRestaurant[plan.Restaurants.Count];
        int reused = 0;

        await Parallel.ForEachAsync(
            plan.Restaurants,
            new ParallelOptions { MaxDegreeOfParallelism = options.Parallelism, CancellationToken = cancellationToken },
            async (spec, token) =>
            {
                bool exists = existingByTaxId.TryGetValue(spec.TaxIdentification, out RestaurantSummary? summary);

                Guid restaurantId = exists
                    ? summary!.Id
                    : await client.OnboardRestaurantAsync(spec, adminToken, token);

                IReadOnlyList<Guid> menuItemIds = exists
                    ? await ExistingMenuItemIdsAsync(restaurantId, adminToken, token)
                    : [];

                // Also covers the half-seeded case: a restaurant onboarded by a run that died before
                // it wrote the menu has no items, and a fixture pointing at an empty menu produces a
                // load test where every order fails on MenuItemNotFound.
                if (menuItemIds.Count == 0)
                {
                    menuItemIds = await CreateMenuAsync(restaurantId, spec, adminToken, token);
                }
                else
                {
                    Interlocked.Increment(ref reused);
                }

                string managerPassword = options.SeededPassword;

                await activator.ActivateAsync(spec.ManagerEmail, token);

                seeded[spec.Index] = new FixtureRestaurant(
                    restaurantId,
                    spec.Name,
                    spec.Latitude,
                    spec.Longitude,
                    spec.City,
                    spec.PostalCode,
                    spec.Country,
                    spec.ManagerEmail,
                    managerPassword,
                    menuItemIds);
            });

        Log.Done($"restaurants ready: {seeded.Length} ({reused} already existed), " +
                 $"{seeded.Sum(restaurant => restaurant.MenuItemIds.Count)} menu items");

        return seeded;
    }

    private async Task<IReadOnlyList<Guid>> ExistingMenuItemIdsAsync(
        Guid restaurantId,
        string adminToken,
        CancellationToken cancellationToken)
    {
        MenuResponse? menu = await client.GetMenuAsync(restaurantId, adminToken, cancellationToken);

        if (menu is null)
        {
            return [];
        }

        return menu.Categories
            .SelectMany(category => category.Items)
            .Where(item => item.IsAvailable)
            .Select(item => item.Id)
            .ToArray();
    }

    private async Task<IReadOnlyList<Guid>> CreateMenuAsync(
        Guid restaurantId,
        RestaurantSpec spec,
        string adminToken,
        CancellationToken cancellationToken)
    {
        var itemIds = new List<Guid>(spec.Categories.Sum(category => category.Items.Count));

        // Sequential per restaurant, parallel across them: every write here lands on the same
        // aggregate, and hammering one row from several connections buys nothing but lock waits.
        foreach (CategorySpec category in spec.Categories)
        {
            Guid categoryId = await client.CreateMenuCategoryAsync(restaurantId, category, adminToken, cancellationToken);

            foreach (MenuItemSpec item in category.Items)
            {
                itemIds.Add(await client.CreateMenuItemAsync(restaurantId, categoryId, item, adminToken, cancellationToken));
            }
        }

        return itemIds;
    }
}
