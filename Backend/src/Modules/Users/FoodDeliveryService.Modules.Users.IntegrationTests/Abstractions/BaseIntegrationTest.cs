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

    public void Dispose()
    {
        _scope.Dispose();

        GC.SuppressFinalize(this);
    }
}
