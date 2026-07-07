using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

/// <summary>
/// Child of a <see cref="MenuCategory"/> inside the <see cref="Restaurant"/> aggregate. Price is a
/// plain decimal with a platform-default currency for this iteration (a Money value object can
/// replace it when multi-currency arrives). PhotoUrl is a URL only — the upload flow is out of
/// scope. Mutations are internal and driven by the aggregate root; the mutators report whether
/// state actually changed so the root only raises events for real changes.
/// </summary>
public sealed class MenuItem : Entity
{
    private MenuItem()
    {
    }

    public Guid Id { get; private set; }

    public Guid RestaurantId { get; private set; }

    public Guid CategoryId { get; private set; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    public decimal Price { get; private set; }

    public string? PhotoUrl { get; private set; }

    public bool IsAvailable { get; private set; }

    internal static Result<MenuItem> Create(
        Guid restaurantId,
        Guid categoryId,
        string name,
        string description,
        decimal price,
        string? photoUrl,
        bool isAvailable)
    {
        if (price <= 0)
        {
            return Result.Failure<MenuItem>(MenuItemErrors.InvalidPrice);
        }

        return new MenuItem
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            CategoryId = categoryId,
            Name = name,
            Description = description,
            Price = price,
            PhotoUrl = photoUrl,
            IsAvailable = isAvailable
        };
    }

    internal bool UpdateDetails(string name, string description, string? photoUrl)
    {
        if (Name == name && Description == description && PhotoUrl == photoUrl)
        {
            return false;
        }

        Name = name;
        Description = description;
        PhotoUrl = photoUrl;

        return true;
    }

    internal Result<bool> ChangePrice(decimal price)
    {
        if (price <= 0)
        {
            return Result.Failure<bool>(MenuItemErrors.InvalidPrice);
        }

        if (Price == price)
        {
            return false;
        }

        Price = price;

        return true;
    }

    internal bool SetAvailability(bool isAvailable)
    {
        if (IsAvailable == isAvailable)
        {
            return false;
        }

        IsAvailable = isAvailable;

        return true;
    }
}
