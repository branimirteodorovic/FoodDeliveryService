using System.Text.Json;
using System.Text.Json.Serialization;

namespace FoodDeliveryService.LoadTest.Seeder;

/// <summary>
/// What the k6 scripts read (`loadtest/lib/fixtures.js`): the ids and credentials of the world this
/// tool created. Gitignored — it holds throwaway passwords and ids that only mean anything against
/// one database — with `seed.sample.json` committed next to it so the shape stays reviewable.
/// </summary>
internal sealed record SeedFixture(
    string RunId,
    DateTime GeneratedOnUtc,
    string Environment,
    string Prefix,
    IReadOnlyList<FixtureRestaurant> Restaurants,
    IReadOnlyList<FixtureCustomer> Customers,
    IReadOnlyList<FixtureDriver> Drivers)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
    };

    public static SeedFixture Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new SeederException(
                $"no fixture at {path}. Run the seeder without --verify first.");
        }

        SeedFixture? fixture = JsonSerializer.Deserialize<SeedFixture>(File.ReadAllText(path), SerializerOptions);

        return fixture ?? throw new SeederException($"{path} is not a fixture this tool wrote.");
    }

    public void Save(string path)
    {
        string? directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, SerializerOptions));
    }
}

/// <summary>
/// A seeded restaurant and everything a scenario needs to order from it: the menu item ids
/// `order.js` picks lines from, and coordinates near the restaurant for the delivery address (the
/// assignment search is a radius around the restaurant, so a delivery pin in another city is a
/// silently unassignable order).
/// <para>
/// The manager credential is here because the account is provisioned by onboarding anyway, and
/// Milestone C's restaurant-side progression (`Pending → Accepted → Preparing → Ready`) needs
/// somebody to log in as. Seeding it now costs one activation per restaurant and saves a re-seed.
/// </para>
/// </summary>
internal sealed record FixtureRestaurant(
    Guid RestaurantId,
    string Name,
    double Latitude,
    double Longitude,
    string City,
    string PostalCode,
    string Country,
    string ManagerEmail,
    string ManagerPassword,
    IReadOnlyList<Guid> MenuItemIds);

internal sealed record FixtureCustomer(string Email, string Password);

internal sealed record FixtureDriver(Guid DriverId, string Email, string Password);
