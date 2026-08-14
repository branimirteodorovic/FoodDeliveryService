using System.Globalization;
using Bogus;

namespace FoodDeliveryService.LoadTest.Seeder;

/// <summary>
/// The whole dataset, decided before a single HTTP call is made.
/// <para>
/// Generating everything up front is what makes the run deterministic: the same
/// <c>--random-seed</c> produces the same catalogue, the same driver positions and the same
/// customer list, no matter how many requests end up running in parallel afterwards. Generate
/// inside the parallel loops instead and the "deterministic" in this milestone's title stops being
/// true the first time the thread pool schedules differently.
/// </para>
/// </summary>
internal sealed record SeedPlan(
    IReadOnlyList<RestaurantSpec> Restaurants,
    IReadOnlyList<DriverSpec> Drivers,
    IReadOnlyList<CustomerSpec> Customers)
{
    private static readonly string[] CuisineTypes =
        ["Italian", "Serbian", "Japanese", "Mexican", "Indian", "Greek", "Vegan", "Burgers"];

    private static readonly string[] CategoryNames =
        ["Starters", "Mains", "Desserts", "Drinks", "Sides", "Specials"];

    private static readonly string[] VehicleTypes = ["Bicycle", "Motorcycle", "Car"];

    public static SeedPlan Build(SeederOptions options)
    {
        // One Faker, one seeded Randomizer, consumed in a fixed order.
        var faker = new Faker { Random = new Randomizer(options.RandomSeed) };

        var restaurants = new List<RestaurantSpec>(options.Restaurants);

        for (int index = 0; index < options.Restaurants; index++)
        {
            restaurants.Add(BuildRestaurant(faker, options, index));
        }

        var drivers = new List<DriverSpec>(options.Drivers);

        for (int index = 0; index < options.Drivers; index++)
        {
            // Half the restaurant spread: every driver is then comfortably inside the 5 km radius
            // the assignment search uses around any of them.
            (double latitude, double longitude) = Scatter(faker, options, options.SpreadKm / 2);

            drivers.Add(new DriverSpec(
                index,
                Email(options, "driver", index),
                faker.Name.FirstName(),
                faker.Name.LastName(),
                faker.Random.ArrayElement(VehicleTypes),
                latitude,
                longitude));
        }

        var customers = new List<CustomerSpec>(options.Customers);

        for (int index = 0; index < options.Customers; index++)
        {
            customers.Add(new CustomerSpec(
                index,
                Email(options, "customer", index),
                faker.Name.FirstName(),
                faker.Name.LastName()));
        }

        return new SeedPlan(restaurants, drivers, customers);
    }

    /// <summary>
    /// The throwaway account the replica probe orders with. Registered last on purpose — see
    /// <see cref="ReplicaProbe"/>.
    /// </summary>
    public static CustomerSpec ProbeCustomer(SeederOptions options) =>
        new(-1, Email(options, "probe", 0), "Replica", "Probe");

    private static RestaurantSpec BuildRestaurant(Faker faker, SeederOptions options, int index)
    {
        (double latitude, double longitude) = Scatter(faker, options, options.SpreadKm);

        var categories = new List<CategorySpec>(options.CategoriesPerRestaurant);

        for (int categoryIndex = 0; categoryIndex < options.CategoriesPerRestaurant; categoryIndex++)
        {
            var items = new List<MenuItemSpec>(options.ItemsPerCategory);

            for (int itemIndex = 0; itemIndex < options.ItemsPerCategory; itemIndex++)
            {
                items.Add(new MenuItemSpec(
                    $"{faker.Commerce.ProductAdjective()} {faker.Commerce.ProductMaterial()} {itemIndex + 1}",
                    faker.Commerce.ProductDescription(),
                    decimal.Round(faker.Random.Decimal(3m, 40m), 2)));
            }

            categories.Add(new CategorySpec(
                CategoryNames[categoryIndex % CategoryNames.Length],
                categoryIndex,
                items));
        }

        return new RestaurantSpec(
            index,

            // The idempotency key of the whole catalogue: a re-run matches on this and skips what
            // already exists rather than onboarding a second copy.
            TaxIdentification: $"{options.Prefix.ToUpperInvariant()}-{index:D4}",
            Name: $"{options.Prefix} {faker.Company.CompanyName()} {index:D3}",
            CuisineType: faker.Random.ArrayElement(CuisineTypes),
            Email: Email(options, "restaurant", index),
            PhoneNumber: $"+3811{index:D7}",
            Street: $"{faker.Address.StreetName()} {faker.Random.Int(1, 120)}",
            City: faker.Address.City(),
            PostalCode: faker.Random.Int(10_000, 99_999).ToString(CultureInfo.InvariantCulture),
            Country: "Serbia",
            Latitude: latitude,
            Longitude: longitude,
            CommissionRate: 0.20m,
            ManagerEmail: Email(options, "manager", index),
            ManagerFirstName: faker.Name.FirstName(),
            ManagerLastName: faker.Name.LastName(),
            Categories: categories);
    }

    /// <summary>
    /// A point inside <paramref name="radiusKm"/> of the configured centre. Flat-earth arithmetic,
    /// which is correct to well under a metre at these distances and keeps the tool free of a
    /// geodesy dependency.
    /// </summary>
    private static (double Latitude, double Longitude) Scatter(Faker faker, SeederOptions options, double radiusKm)
    {
        const double kilometresPerDegreeLatitude = 111.0;

        double latitudeOffset = faker.Random.Double(-1, 1) * radiusKm / kilometresPerDegreeLatitude;

        double longitudeOffset = faker.Random.Double(-1, 1) * radiusKm /
            (kilometresPerDegreeLatitude * Math.Cos(options.CenterLatitude * Math.PI / 180));

        return (options.CenterLatitude + latitudeOffset, options.CenterLongitude + longitudeOffset);
    }

    private static string Email(SeederOptions options, string role, int index) =>
        $"{options.Prefix}-{role}-{index:D4}@fooddeliveryservice.com";
}

internal sealed record RestaurantSpec(
    int Index,
    string TaxIdentification,
    string Name,
    string CuisineType,
    string Email,
    string PhoneNumber,
    string Street,
    string City,
    string PostalCode,
    string Country,
    double Latitude,
    double Longitude,
    decimal CommissionRate,
    string ManagerEmail,
    string ManagerFirstName,
    string ManagerLastName,
    IReadOnlyList<CategorySpec> Categories);

internal sealed record CategorySpec(string Name, int DisplayOrder, IReadOnlyList<MenuItemSpec> Items);

internal sealed record MenuItemSpec(string Name, string Description, decimal Price);

internal sealed record DriverSpec(
    int Index,
    string Email,
    string FirstName,
    string LastName,
    string VehicleType,
    double Latitude,
    double Longitude);

internal sealed record CustomerSpec(int Index, string Email, string FirstName, string LastName);
