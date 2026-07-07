using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

/// <summary>
/// Aggregate root for a restaurant and its menu (categories + items). Created by an Administrator
/// during onboarding — <see cref="ManagerUserId"/> is the RestaurantManager user provisioned in the
/// Users service via the ProvisionManagerUserRequest RPC. A manager may run multiple restaurants,
/// so ManagerUserId is deliberately not unique. All menu mutations go through this root so the
/// ownership check and menu invariants (duplicate names, category existence) live in one place.
/// </summary>
public sealed class Restaurant : Entity
{
    private readonly List<MenuCategory> _menuCategories = [];

    private Restaurant()
    {
    }

    public Guid Id { get; private set; }

    public Guid ManagerUserId { get; private set; }

    public string Name { get; private set; }

    public string TaxIdentification { get; private set; }

    public string CuisineType { get; private set; }

    public string Email { get; private set; }

    public string PhoneNumber { get; private set; }

    public Address Address { get; private set; }

    // Per-restaurant commercial term supplied by the Administrator at onboarding; a fraction in
    // [0, 1) (0.20 = 20%). The Orders service will read it later to split each order total.
    public decimal CommissionRate { get; private set; }

    public RestaurantStatus Status { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public IReadOnlyCollection<MenuCategory> MenuCategories => _menuCategories.ToList();

    public static Result<Restaurant> Create(
        Guid managerUserId,
        string name,
        string taxIdentification,
        string cuisineType,
        string email,
        string phoneNumber,
        Address address,
        decimal commissionRate,
        DateTime createdOnUtc)
    {
        if (commissionRate is < 0 or >= 1)
        {
            return Result.Failure<Restaurant>(RestaurantErrors.InvalidCommissionRate);
        }

        var restaurant = new Restaurant
        {
            Id = Guid.NewGuid(),
            ManagerUserId = managerUserId,
            Name = name,
            TaxIdentification = taxIdentification,
            CuisineType = cuisineType,
            Email = email,
            PhoneNumber = phoneNumber,
            Address = address,
            CommissionRate = commissionRate,
            Status = RestaurantStatus.Active,
            CreatedOnUtc = createdOnUtc
        };

        restaurant.Raise(new RestaurantRegisteredDomainEvent(restaurant.Id));

        return restaurant;
    }

    public Result UpdateDetails(string name, string taxIdentification, string cuisineType, string email, string phoneNumber)
    {
        if (Name == name &&
            TaxIdentification == taxIdentification &&
            CuisineType == cuisineType &&
            Email == email &&
            PhoneNumber == phoneNumber)
        {
            return Result.Success();
        }

        Name = name;
        TaxIdentification = taxIdentification;
        CuisineType = cuisineType;
        Email = email;
        PhoneNumber = phoneNumber;

        Raise(new RestaurantDetailsUpdatedDomainEvent(Id));

        return Result.Success();
    }

    public Result UpdateAddress(Address address)
    {
        if (Address == address)
        {
            return Result.Success();
        }

        Address = address;

        Raise(new RestaurantAddressUpdatedDomainEvent(Id));

        return Result.Success();
    }

    public Result<MenuCategory> AddMenuCategory(string name, int displayOrder)
    {
        if (_menuCategories.Any(category => string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<MenuCategory>(MenuCategoryErrors.DuplicateName(name));
        }

        var category = MenuCategory.Create(Id, name, displayOrder);

        _menuCategories.Add(category);

        Raise(new MenuCategoryAddedDomainEvent(Id, category.Id));

        return category;
    }

    public Result UpdateMenuCategory(Guid categoryId, string name, int displayOrder)
    {
        MenuCategory? category = _menuCategories.SingleOrDefault(c => c.Id == categoryId);

        if (category is null)
        {
            return Result.Failure(MenuCategoryErrors.NotFound(categoryId));
        }

        if (_menuCategories.Any(c => c.Id != categoryId && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure(MenuCategoryErrors.DuplicateName(name));
        }

        category.Update(name, displayOrder);

        Raise(new MenuCategoryUpdatedDomainEvent(Id, categoryId));

        return Result.Success();
    }

    public Result<MenuItem> AddMenuItem(
        Guid categoryId,
        string name,
        string description,
        decimal price,
        string? photoUrl,
        bool isAvailable)
    {
        MenuCategory? category = _menuCategories.SingleOrDefault(c => c.Id == categoryId);

        if (category is null)
        {
            return Result.Failure<MenuItem>(MenuCategoryErrors.NotFound(categoryId));
        }

        Result<MenuItem> itemResult = MenuItem.Create(Id, categoryId, name, description, price, photoUrl, isAvailable);

        if (itemResult.IsFailure)
        {
            return itemResult;
        }

        category.AddItem(itemResult.Value);

        Raise(new MenuItemAddedDomainEvent(Id, itemResult.Value.Id));

        return itemResult;
    }

    public Result UpdateMenuItem(Guid menuItemId, string name, string description, decimal price, string? photoUrl)
    {
        MenuItem? item = FindMenuItem(menuItemId);

        if (item is null)
        {
            return Result.Failure(MenuItemErrors.NotFound(menuItemId));
        }

        if (item.UpdateDetails(name, description, photoUrl))
        {
            Raise(new MenuItemUpdatedDomainEvent(Id, menuItemId));
        }

        Result<bool> priceResult = item.ChangePrice(price);

        if (priceResult.IsFailure)
        {
            return Result.Failure(priceResult.Error);
        }

        if (priceResult.Value)
        {
            Raise(new MenuItemPriceChangedDomainEvent(Id, menuItemId, price));
        }

        return Result.Success();
    }

    public Result SetMenuItemAvailability(Guid menuItemId, bool isAvailable)
    {
        MenuItem? item = FindMenuItem(menuItemId);

        if (item is null)
        {
            return Result.Failure(MenuItemErrors.NotFound(menuItemId));
        }

        if (item.SetAvailability(isAvailable))
        {
            Raise(new MenuItemAvailabilityChangedDomainEvent(Id, menuItemId, isAvailable));
        }

        return Result.Success();
    }

    private MenuItem? FindMenuItem(Guid menuItemId) =>
        _menuCategories.SelectMany(c => c.Items).SingleOrDefault(i => i.Id == menuItemId);
}
