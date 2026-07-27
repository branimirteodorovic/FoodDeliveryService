using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Caching;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.CreateMenuCategory;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.CreateMenuItem;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.SetMenuItemAvailability;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.UpdateMenuCategory;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.UpdateMenuItem;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;
using FoodDeliveryService.Modules.Restaurants.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.Restaurants.UnitTests.Restaurants;

/// <summary>
/// Milestone C of the caching plan: every command that changes what <c>GET restaurants/{id}/menu</c>
/// returns must evict <c>restaurants:menu:{id}</c> inline, so the next read is fresh instead of
/// waiting out the 5-minute TTL. The key is always built through <see cref="RestaurantCacheKeys"/>
/// — the same helper the cached query uses — so reads and evictions can't drift apart.
/// </summary>
public class MenuCacheInvalidationTests : BaseTest
{
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly RecordingCacheService _cacheService = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task CreateMenuCategory_Should_EvictTheMenuKey()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        var handler = new CreateMenuCategoryCommandHandler(
            new FakeRestaurantsRepository(restaurant),
            new FakeRestaurantsContext(_managerUserId),
            _unitOfWork,
            _cacheService);

        // Act
        Result<Guid> result = await handler.Handle(
            new CreateMenuCategoryCommand(restaurant.Id, "Desserts", 2),
            TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _cacheService.RemovedKeys.Should().Equal(RestaurantCacheKeys.Menu(restaurant.Id));
    }

    [Fact]
    public async Task UpdateMenuCategory_Should_EvictTheMenuKey()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        Guid categoryId = AddCategory(restaurant);
        var handler = new UpdateMenuCategoryCommandHandler(
            new FakeRestaurantsRepository(restaurant),
            new FakeRestaurantsContext(_managerUserId),
            _unitOfWork,
            _cacheService);

        // Act
        Result result = await handler.Handle(
            new UpdateMenuCategoryCommand(restaurant.Id, categoryId, "Renamed", 5),
            TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _cacheService.RemovedKeys.Should().Equal(RestaurantCacheKeys.Menu(restaurant.Id));
    }

    [Fact]
    public async Task CreateMenuItem_Should_EvictTheMenuKey()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        Guid categoryId = AddCategory(restaurant);
        var handler = new CreateMenuItemCommandHandler(
            new FakeRestaurantsRepository(restaurant),
            new FakeRestaurantsContext(_managerUserId),
            _unitOfWork,
            _cacheService);

        // Act
        Result<Guid> result = await handler.Handle(
            new CreateMenuItemCommand(restaurant.Id, categoryId, "Tiramisu", "Coffee dessert", 6.5m, null, true),
            TestContext.Current.CancellationToken);

        // Assert — only the menu key: the item's own id is brand new, so nothing is cached under it.
        result.IsSuccess.Should().BeTrue();
        _cacheService.RemovedKeys.Should().Equal(RestaurantCacheKeys.Menu(restaurant.Id));
    }

    [Fact]
    public async Task UpdateMenuItem_Should_EvictBothTheItemAndMenuKeys()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        Guid categoryId = AddCategory(restaurant);
        Guid menuItemId = AddItem(restaurant, categoryId);
        var handler = new UpdateMenuItemCommandHandler(
            new FakeRestaurantsRepository(restaurant),
            new FakeRestaurantsContext(_managerUserId),
            _unitOfWork,
            _cacheService);

        // Act
        Result result = await handler.Handle(
            new UpdateMenuItemCommand(restaurant.Id, menuItemId, "Pizza Margherita", "Now with basil", 14m, null),
            TestContext.Current.CancellationToken);

        // Assert — the item detail key (Milestone B) *and* the composed menu that embeds it.
        result.IsSuccess.Should().BeTrue();
        _cacheService.RemovedKeys.Should().Equal(
            RestaurantCacheKeys.Item(menuItemId),
            RestaurantCacheKeys.Menu(restaurant.Id));
    }

    [Fact]
    public async Task SetMenuItemAvailability_Should_EvictBothTheItemAndMenuKeys()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        Guid categoryId = AddCategory(restaurant);
        Guid menuItemId = AddItem(restaurant, categoryId);
        var handler = new SetMenuItemAvailabilityCommandHandler(
            new FakeRestaurantsRepository(restaurant),
            new FakeRestaurantsContext(_managerUserId),
            _unitOfWork,
            _cacheService);

        // Act — sell the item out.
        Result result = await handler.Handle(
            new SetMenuItemAvailabilityCommand(restaurant.Id, menuItemId, IsAvailable: false),
            TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _cacheService.RemovedKeys.Should().Equal(
            RestaurantCacheKeys.Item(menuItemId),
            RestaurantCacheKeys.Menu(restaurant.Id));
    }

    [Fact]
    public async Task MenuCommand_Should_NotEvictAnything_WhenItFails()
    {
        // Arrange — the repository resolves nothing for this id, so the handler short-circuits
        // before SaveChangesAsync. A failed write must never punch a hole in a valid cache entry.
        var handler = new CreateMenuCategoryCommandHandler(
            new FakeRestaurantsRepository(seed: null),
            new FakeRestaurantsContext(_managerUserId),
            _unitOfWork,
            _cacheService);

        // Act
        Result<Guid> result = await handler.Handle(
            new CreateMenuCategoryCommand(Guid.NewGuid(), "Desserts", 2),
            TestContext.Current.CancellationToken);

        // Assert
        result.IsFailure.Should().BeTrue();
        _unitOfWork.SaveChangesCallCount.Should().Be(0);
        _cacheService.RemovedKeys.Should().BeEmpty();
    }

    private Restaurant CreateRestaurant()
    {
        Result<Restaurant> result = Restaurant.Create(
            _managerUserId,
            Faker.Company.CompanyName(),
            Faker.Random.Replace("##########"),
            "Italian",
            Faker.Internet.Email(),
            Faker.Phone.PhoneNumber(),
            new Address(
                Faker.Address.StreetAddress(),
                Faker.Address.City(),
                Faker.Address.ZipCode(),
                Faker.Address.Country(),
                Faker.Address.Latitude(),
                Faker.Address.Longitude()),
            0.2m,
            DateTime.UtcNow);

        return result.Value;
    }

    private static Guid AddCategory(Restaurant restaurant) =>
        restaurant.AddMenuCategory("Mains", 1).Value.Id;

    private static Guid AddItem(Restaurant restaurant, Guid categoryId) =>
        restaurant.AddMenuItem(categoryId, "Pizza", "Tomato and mozzarella", 12.5m, null, isAvailable: true).Value.Id;
}
