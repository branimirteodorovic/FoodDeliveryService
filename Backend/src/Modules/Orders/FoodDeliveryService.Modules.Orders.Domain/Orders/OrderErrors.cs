using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Orders.Domain.Orders;

public static class OrderErrors
{
    public static Error NotFound(Guid orderId) =>
        Error.NotFound(
            "Orders.NotFound",
            $"The order with the identifier {orderId} was not found");

    public static readonly Error Empty = Error.Problem(
        "Orders.Empty",
        "An order must contain at least one line item");

    // The caller's user id has no Customer replica row yet — either the token belongs to a
    // non-customer principal or the UserRegistered event has not been consumed yet.
    public static Error CustomerNotFound(Guid customerId) =>
        Error.NotFound(
            "Orders.CustomerNotFound",
            $"The customer with the identifier {customerId} was not found");

    public static Error RestaurantNotFound(Guid restaurantId) =>
        Error.NotFound(
            "Orders.RestaurantNotFound",
            $"The restaurant with the identifier {restaurantId} was not found");

    public static Error MenuItemNotFound(Guid menuItemId) =>
        Error.NotFound(
            "Orders.MenuItemNotFound",
            $"The menu item with the identifier {menuItemId} was not found on the restaurant's menu");

    public static Error MenuItemUnavailable(Guid menuItemId) =>
        Error.Problem(
            "Orders.MenuItemUnavailable",
            $"The menu item with the identifier {menuItemId} is currently unavailable");

    public static Error InvalidTransition(OrderStatus from, OrderStatus to) =>
        Error.Problem(
            "Orders.InvalidTransition",
            $"The order cannot move from status {from} to status {to}");

    public static readonly Error NotOwner = Error.Problem(
        "Orders.NotOwner",
        "Only the order's customer or the restaurant's manager can perform this action");

    public static readonly Error DuplicateIdempotencyKey = Error.Conflict(
        "Orders.DuplicateIdempotencyKey",
        "An order with the same idempotency key already exists");

    // The delivery address must carry coordinates so the Delivery service can route to the dropoff.
    public static readonly Error MissingCoordinates = Error.Problem(
        "Orders.MissingCoordinates",
        "The delivery address must include a latitude and a longitude");

    public static readonly Error InvalidCoordinates = Error.Problem(
        "Orders.InvalidCoordinates",
        "The latitude must be between -90 and 90 and the longitude between -180 and 180");
}
