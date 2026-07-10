using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;
using FoodDeliveryService.Modules.Restaurants.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.Restaurants.UnitTests.Restaurants;

public class RestaurantsTests : BaseTest
{
    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Create_ShouldReturnFailure_WhenCommissionRateIsOutOfBounds(decimal commissionRate)
    {
        // Arrange
        var managerUserId = Guid.NewGuid();
        var name = "Marios Pizerria";
        var taxIdentification = Faker.Finance.Random.AlphaNumeric(10);
        var cuisineType = "Italian";
        var email = Faker.Person.Email;
        var phoneNumber = Faker.Person.Phone;
        var address = new Address(Faker.Address.StreetAddress(), Faker.Address.City(), Faker.Address.ZipCode(), Faker.Address.Country(), Faker.Address.Latitude(), Faker.Address.Longitude());
        var createdOnUtc = DateTime.Now;

        // Act
        var result = Restaurant.Create(managerUserId, name, taxIdentification, cuisineType, email, phoneNumber, address, commissionRate, createdOnUtc);

        // Assert
        result.Error.Should().Be(RestaurantErrors.InvalidCommissionRate);
    }

    [Fact]
    public void Create_ShouldRaiseDomainEvent_WhenRestaurantIsCreated()
    {
        // Arrange
        var managerUserId = Guid.NewGuid();
        var name = "Marios Pizerria";
        var taxIdentification = Faker.Finance.Random.AlphaNumeric(10);
        var cuisineType = "Italian";
        var email = Faker.Person.Email;
        var phoneNumber = Faker.Person.Phone;
        var address = new Address(Faker.Address.StreetAddress(), Faker.Address.City(), Faker.Address.ZipCode(), Faker.Address.Country(), Faker.Address.Latitude(), Faker.Address.Longitude());
        var commissionRate = 0.3m;
        var createdOnUtc = DateTime.Now;

        // Act
        Result<Restaurant> result = Restaurant.Create(managerUserId, name, taxIdentification, cuisineType, email, phoneNumber, address, commissionRate, createdOnUtc);
        Restaurant restaurant = result.Value;

        // Assert
        RestaurantRegisteredDomainEvent domainEvent = AssertDomainEventWasPublished<RestaurantRegisteredDomainEvent>(restaurant);
        domainEvent.RestaurantId.Should().Be(restaurant.Id);
    }

    [Fact]
    public void Create_ShouldHaveActiveStatus_WhenRestaurantIsCreated()
    {
        // Arrange & Act
        Restaurant restaurant = CreateRestaurant();

        // Assert
        restaurant.Status.Should().Be(RestaurantStatus.Active);
    }

    [Fact]
    public void UpdateDetails_ShouldUpdateFieldsAndRaiseDomainEvent_WhenDetailsChanged()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        var name = "New Name";
        var taxIdentification = Faker.Finance.Random.AlphaNumeric(10);
        var cuisineType = "Mexican";
        var email = Faker.Person.Email;
        var phoneNumber = Faker.Person.Phone;

        // Act
        Result result = restaurant.UpdateDetails(name, taxIdentification, cuisineType, email, phoneNumber);

        // Assert
        result.IsSuccess.Should().BeTrue();
        restaurant.Name.Should().Be(name);
        restaurant.TaxIdentification.Should().Be(taxIdentification);
        restaurant.CuisineType.Should().Be(cuisineType);
        restaurant.Email.Should().Be(email);
        restaurant.PhoneNumber.Should().Be(phoneNumber);
        AssertDomainEventWasPublished<RestaurantDetailsUpdatedDomainEvent>(restaurant);
    }

    [Fact]
    public void UpdateDetails_ShouldNotRaiseDomainEvent_WhenDetailsAreUnchanged()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant(
            out string name,
            out string taxIdentification,
            out string cuisineType,
            out string email,
            out string phoneNumber);

        // Act
        Result result = restaurant.UpdateDetails(name, taxIdentification, cuisineType, email, phoneNumber);

        // Assert
        result.IsSuccess.Should().BeTrue();
        restaurant.DomainEvents.OfType<RestaurantDetailsUpdatedDomainEvent>().Should().BeEmpty();
    }

    [Fact]
    public void UpdateAddress_ShouldUpdateAddressAndRaiseDomainEvent_WhenAddressChanged()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        var newAddress = new Address(Faker.Address.StreetAddress(), Faker.Address.City(), Faker.Address.ZipCode(), Faker.Address.Country(), Faker.Address.Latitude(), Faker.Address.Longitude());

        // Act
        Result result = restaurant.UpdateAddress(newAddress);

        // Assert
        result.IsSuccess.Should().BeTrue();
        restaurant.Address.Should().Be(newAddress);
        AssertDomainEventWasPublished<RestaurantAddressUpdatedDomainEvent>(restaurant);
    }

    [Fact]
    public void UpdateAddress_ShouldNotRaiseDomainEvent_WhenAddressIsUnchanged()
    {
        // Arrange
        Address address = new(Faker.Address.StreetAddress(), Faker.Address.City(), Faker.Address.ZipCode(), Faker.Address.Country(), Faker.Address.Latitude(), Faker.Address.Longitude());
        Restaurant restaurant = CreateRestaurant(address: address);

        // Act
        Result result = restaurant.UpdateAddress(address);

        // Assert
        result.IsSuccess.Should().BeTrue();
        restaurant.DomainEvents.OfType<RestaurantAddressUpdatedDomainEvent>().Should().BeEmpty();
    }

    [Fact]
    public void AddMenuCategory_ShouldAddCategoryAndRaiseDomainEvent_WhenNameIsUnique()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        var name = "Starters";
        var displayOrder = 1;

        // Act
        Result<MenuCategory> result = restaurant.AddMenuCategory(name, displayOrder);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be(name);
        result.Value.DisplayOrder.Should().Be(displayOrder);
        result.Value.RestaurantId.Should().Be(restaurant.Id);
        restaurant.MenuCategories.Should().ContainSingle(c => c.Id == result.Value.Id);
        MenuCategoryAddedDomainEvent domainEvent = AssertDomainEventWasPublished<MenuCategoryAddedDomainEvent>(restaurant);
        domainEvent.RestaurantId.Should().Be(restaurant.Id);
        domainEvent.CategoryId.Should().Be(result.Value.Id);
    }

    [Fact]
    public void AddMenuCategory_ShouldReturnFailure_WhenNameIsDuplicate()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        restaurant.AddMenuCategory("Starters", 1);

        // Act
        Result<MenuCategory> result = restaurant.AddMenuCategory("STARTERS", 2);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MenuCategoryErrors.DuplicateName("STARTERS"));
        restaurant.MenuCategories.Should().ContainSingle();
    }

    [Fact]
    public void UpdateMenuCategory_ShouldReturnFailure_WhenCategoryNotFound()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        var categoryId = Guid.NewGuid();

        // Act
        Result result = restaurant.UpdateMenuCategory(categoryId, "Mains", 1);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MenuCategoryErrors.NotFound(categoryId));
    }

    [Fact]
    public void UpdateMenuCategory_ShouldReturnFailure_WhenNameConflictsWithAnotherCategory()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        MenuCategory starters = restaurant.AddMenuCategory("Starters", 1).Value;
        restaurant.AddMenuCategory("Mains", 2);

        // Act
        Result result = restaurant.UpdateMenuCategory(starters.Id, "MAINS", 1);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MenuCategoryErrors.DuplicateName("MAINS"));
    }

    [Fact]
    public void UpdateMenuCategory_ShouldUpdateCategoryAndRaiseDomainEvent_WhenValid()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        MenuCategory category = restaurant.AddMenuCategory("Starters", 1).Value;
        var newName = "Appetizers";
        var newDisplayOrder = 5;

        // Act
        Result result = restaurant.UpdateMenuCategory(category.Id, newName, newDisplayOrder);

        // Assert
        result.IsSuccess.Should().BeTrue();
        category.Name.Should().Be(newName);
        category.DisplayOrder.Should().Be(newDisplayOrder);
        AssertDomainEventWasPublished<MenuCategoryUpdatedDomainEvent>(restaurant);
    }

    [Fact]
    public void UpdateMenuCategory_ShouldSucceed_WhenNameIsUnchangedForSameCategory()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        MenuCategory category = restaurant.AddMenuCategory("Starters", 1).Value;

        // Act
        Result result = restaurant.UpdateMenuCategory(category.Id, "Starters", 2);

        // Assert
        result.IsSuccess.Should().BeTrue();
        category.DisplayOrder.Should().Be(2);
    }

    [Fact]
    public void AddMenuItem_ShouldReturnFailure_WhenCategoryNotFound()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        var categoryId = Guid.NewGuid();

        // Act
        Result<MenuItem> result = restaurant.AddMenuItem(categoryId, "Pizza", "Description", 9.99m, null, true);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MenuCategoryErrors.NotFound(categoryId));
    }

    [Fact]
    public void AddMenuItem_ShouldReturnFailure_WhenPriceIsInvalid()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        MenuCategory category = restaurant.AddMenuCategory("Mains", 1).Value;

        // Act
        Result<MenuItem> result = restaurant.AddMenuItem(category.Id, "Pizza", "Description", 0m, null, true);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MenuItemErrors.InvalidPrice);
        category.Items.Should().BeEmpty();
    }

    [Fact]
    public void AddMenuItem_ShouldAddItemAndRaiseDomainEvent_WhenValid()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        MenuCategory category = restaurant.AddMenuCategory("Mains", 1).Value;
        var name = "Pizza";
        var description = "Cheese pizza";
        var price = 12.5m;
        var photoUrl = "https://example.com/pizza.png";

        // Act
        Result<MenuItem> result = restaurant.AddMenuItem(category.Id, name, description, price, photoUrl, true);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be(name);
        result.Value.Description.Should().Be(description);
        result.Value.Price.Should().Be(price);
        result.Value.PhotoUrl.Should().Be(photoUrl);
        result.Value.IsAvailable.Should().BeTrue();
        result.Value.CategoryId.Should().Be(category.Id);
        result.Value.RestaurantId.Should().Be(restaurant.Id);
        category.Items.Should().ContainSingle(i => i.Id == result.Value.Id);
        MenuItemAddedDomainEvent domainEvent = AssertDomainEventWasPublished<MenuItemAddedDomainEvent>(restaurant);
        domainEvent.RestaurantId.Should().Be(restaurant.Id);
        domainEvent.MenuItemId.Should().Be(result.Value.Id);
    }

    [Fact]
    public void UpdateMenuItem_ShouldReturnFailure_WhenItemNotFound()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        var menuItemId = Guid.NewGuid();

        // Act
        Result result = restaurant.UpdateMenuItem(menuItemId, "Pizza", "Description", 9.99m, null);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MenuItemErrors.NotFound(menuItemId));
    }

    [Fact]
    public void UpdateMenuItem_ShouldReturnFailure_WhenPriceIsInvalid()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        MenuItem item = AddMenuItem(restaurant);

        // Act
        Result result = restaurant.UpdateMenuItem(item.Id, item.Name, item.Description, 0m, item.PhotoUrl);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MenuItemErrors.InvalidPrice);
    }

    [Fact]
    public void UpdateMenuItem_ShouldRaiseBothDomainEvents_WhenDetailsAndPriceChange()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        MenuItem item = AddMenuItem(restaurant);
        var newName = "Updated Pizza";
        var newDescription = "Updated description";
        var newPrice = item.Price + 5m;
        var newPhotoUrl = "https://example.com/new-pizza.png";

        // Act
        Result result = restaurant.UpdateMenuItem(item.Id, newName, newDescription, newPrice, newPhotoUrl);

        // Assert
        result.IsSuccess.Should().BeTrue();
        item.Name.Should().Be(newName);
        item.Description.Should().Be(newDescription);
        item.Price.Should().Be(newPrice);
        item.PhotoUrl.Should().Be(newPhotoUrl);
        AssertDomainEventWasPublished<MenuItemUpdatedDomainEvent>(restaurant);
        MenuItemPriceChangedDomainEvent priceChangedEvent = AssertDomainEventWasPublished<MenuItemPriceChangedDomainEvent>(restaurant);
        priceChangedEvent.Price.Should().Be(newPrice);
    }

    [Fact]
    public void UpdateMenuItem_ShouldNotRaiseDomainEvents_WhenNothingChanges()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        MenuItem item = AddMenuItem(restaurant, out string name, out string description, out decimal price, out string? photoUrl);

        // Act
        Result result = restaurant.UpdateMenuItem(item.Id, name, description, price, photoUrl);

        // Assert
        result.IsSuccess.Should().BeTrue();
        restaurant.DomainEvents.OfType<MenuItemUpdatedDomainEvent>().Should().BeEmpty();
        restaurant.DomainEvents.OfType<MenuItemPriceChangedDomainEvent>().Should().BeEmpty();
    }

    [Fact]
    public void SetMenuItemAvailability_ShouldReturnFailure_WhenItemNotFound()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        var menuItemId = Guid.NewGuid();

        // Act
        Result result = restaurant.SetMenuItemAvailability(menuItemId, false);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MenuItemErrors.NotFound(menuItemId));
    }

    [Fact]
    public void SetMenuItemAvailability_ShouldUpdateAvailabilityAndRaiseDomainEvent_WhenValueChanges()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        MenuItem item = AddMenuItem(restaurant, isAvailable: true);

        // Act
        Result result = restaurant.SetMenuItemAvailability(item.Id, false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        item.IsAvailable.Should().BeFalse();
        MenuItemAvailabilityChangedDomainEvent domainEvent = AssertDomainEventWasPublished<MenuItemAvailabilityChangedDomainEvent>(restaurant);
        domainEvent.MenuItemId.Should().Be(item.Id);
        domainEvent.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void SetMenuItemAvailability_ShouldNotRaiseDomainEvent_WhenValueIsUnchanged()
    {
        // Arrange
        Restaurant restaurant = CreateRestaurant();
        MenuItem item = AddMenuItem(restaurant, isAvailable: true);

        // Act
        Result result = restaurant.SetMenuItemAvailability(item.Id, true);

        // Assert
        result.IsSuccess.Should().BeTrue();
        restaurant.DomainEvents.OfType<MenuItemAvailabilityChangedDomainEvent>().Should().BeEmpty();
    }

    private static Restaurant CreateRestaurant(Address? address = null)
    {
        return CreateRestaurant(out _, out _, out _, out _, out _, address);
    }

    private static Restaurant CreateRestaurant(
        out string name,
        out string taxIdentification,
        out string cuisineType,
        out string email,
        out string phoneNumber,
        Address? address = null)
    {
        var managerUserId = Guid.NewGuid();
        name = "Marios Pizerria";
        taxIdentification = Faker.Finance.Random.AlphaNumeric(10);
        cuisineType = "Italian";
        email = Faker.Person.Email;
        phoneNumber = Faker.Person.Phone;
        Address resolvedAddress = address ?? new Address(Faker.Address.StreetAddress(), Faker.Address.City(), Faker.Address.ZipCode(), Faker.Address.Country(), Faker.Address.Latitude(), Faker.Address.Longitude());
        var commissionRate = 0.3m;
        var createdOnUtc = DateTime.Now;

        return Restaurant.Create(managerUserId, name, taxIdentification, cuisineType, email, phoneNumber, resolvedAddress, commissionRate, createdOnUtc).Value;
    }

    private static MenuItem AddMenuItem(Restaurant restaurant, bool isAvailable = true)
    {
        return AddMenuItem(restaurant, out _, out _, out _, out _, isAvailable);
    }

    private static MenuItem AddMenuItem(
        Restaurant restaurant,
        out string name,
        out string description,
        out decimal price,
        out string? photoUrl,
        bool isAvailable = true)
    {
        MenuCategory category = restaurant.AddMenuCategory("Mains", 1).Value;
        name = "Pizza";
        description = "Cheese pizza";
        price = 12.5m;
        photoUrl = "https://example.com/pizza.png";

        return restaurant.AddMenuItem(category.Id, name, description, price, photoUrl, isAvailable).Value;
    }
}
