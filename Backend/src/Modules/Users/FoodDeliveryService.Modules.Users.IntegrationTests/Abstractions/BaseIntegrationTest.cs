using Bogus;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Users.IntegrationTests.Abstractions;

[Collection(nameof(IntegrationTestCollection))]
public class BaseIntegrationTest : IDisposable
{
    protected static readonly Faker Faker = new();
    private readonly IServiceScope _scope;
    protected readonly HttpClient HttpClient;

    public BaseIntegrationTest(IntegrationTestWebAppFactory factory)
    {
        Factory = factory;
        _scope = factory.Services.CreateScope();
        HttpClient = factory.CreateClient();
    }

    protected IntegrationTestWebAppFactory Factory { get; }

    /// <summary>
    /// A globally-unique email. Identity's ASP.NET Identity store is real and persistent (not a
    /// testcontainer), so a fixed or Faker-random address could collide across repeated local runs and
    /// fail registration; the embedded <see cref="Guid"/> guarantees a fresh account every time.
    /// </summary>
    protected static string UniqueEmail() => $"users-tests+{Guid.NewGuid():N}@fooddeliveryservice.com";

    /// <summary>
    /// A password that satisfies ASP.NET Identity's full default strength policy (length, digit,
    /// lower, upper and non-alphanumeric). Identity relaxes those rules only when it runs in the
    /// Development environment; the instance these tests talk to on :18080 does not, so a password
    /// has to clear the strict policy. <c>Faker.Internet.Password</c> cannot: it draws from word
    /// characters only, so it never emits a non-alphanumeric one and randomly omits an uppercase
    /// one — registration then failed intermittently with a 500. Fixed literal, matching what every
    /// other module's fixture uses.
    /// </summary>
    protected const string StrongPassword = "Users-Tests-P@ssw0rd1";

    public void Dispose()
    {
        _scope.Dispose();

        GC.SuppressFinalize(this);
    }
}
