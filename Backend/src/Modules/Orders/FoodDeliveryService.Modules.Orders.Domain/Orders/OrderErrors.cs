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

    public static readonly Error DuplicateIdempotencyKey = Error.Conflict(
        "Orders.DuplicateIdempotencyKey",
        "An order with the same idempotency key already exists");

    // Feature 3.8 Milestone F, §8.2. A card order cannot be accepted until the hold is actually on
    // the card. Retryable and short-lived by design — the authorization window is typically under a
    // second — which is why it is its own error rather than an invalid transition: the restaurant
    // should try again, not conclude that the order is in the wrong state.
    public static readonly Error PaymentNotAuthorized = Error.Problem(
        "Orders.PaymentNotAuthorized",
        "The order cannot be accepted until its payment has been authorized");

    // A payment transition arrived for an order that is not paid by card. Reachable only from a
    // misrouted event, and a failure rather than a silent no-op because that is a bug somewhere.
    public static readonly Error PaymentNotRequired = Error.Problem(
        "Orders.PaymentNotRequired",
        "The order is not paid by card, so it has no payment to transition");

    // The customer asked to pay by card and has no saved card. Checked against the local replica
    // Payments feeds (§6.3), which avoids a synchronous call and is allowed to be a second stale —
    // Payments still does the authoritative check when it authorizes.
    public static readonly Error CardPaymentUnavailable = Error.Problem(
        "Orders.CardPaymentUnavailable",
        "The customer has no saved card, so this order cannot be paid by card");

    // The delivery address must carry coordinates so the Delivery service can route to the dropoff.
    public static readonly Error MissingCoordinates = Error.Problem(
        "Orders.MissingCoordinates",
        "The delivery address must include a latitude and a longitude");

    public static readonly Error InvalidCoordinates = Error.Problem(
        "Orders.InvalidCoordinates",
        "The latitude must be between -90 and 90 and the longitude between -180 and 180");
}
