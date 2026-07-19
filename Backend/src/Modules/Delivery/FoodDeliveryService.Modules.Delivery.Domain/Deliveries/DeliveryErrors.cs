using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Delivery.Domain.Deliveries;

public static class DeliveryErrors
{
    // Guarded by AcceptOffer/RejectOffer/MarkPickedUp/MarkDelivered — only the offered/assigned
    // driver may act on a delivery.
    public static readonly Error NotAssignedDriver = Error.Problem(
        "Deliveries.NotAssignedDriver",
        "Only the driver the delivery was offered or assigned to may perform this action");

    public static readonly Error OfferExpired = Error.Problem(
        "Deliveries.OfferExpired",
        "The offer window for this delivery has already passed");

    // Backstop for ProcessExpiredOffersJob racing an accept/re-offer: an offer that has not lapsed
    // yet cannot be expired.
    public static readonly Error OfferNotExpired = Error.Problem(
        "Deliveries.OfferNotExpired",
        "The offer window for this delivery has not passed yet");

    public static readonly Error NoDriversAvailable = Error.Problem(
        "Deliveries.NoDriversAvailable",
        "No available drivers were found within the search radius");

    // Read-guard for a single delivery: only the order's customer, the assigned driver, or an admin
    // may view it.
    public static readonly Error NotAuthorizedToView = Error.Problem(
        "Deliveries.NotAuthorizedToView",
        "You are not authorized to view this delivery");

    public static Error NotFound(Guid deliveryId) => Error.NotFound(
        "Deliveries.NotFound",
        $"The delivery with the identifier {deliveryId} was not found");

    public static Error NotFoundForOrder(Guid orderId) => Error.NotFound(
        "Deliveries.NotFoundForOrder",
        $"No delivery was found for the order with the identifier {orderId}");

    public static Error AlreadyExists(Guid orderId) => Error.Conflict(
        "Deliveries.AlreadyExists",
        $"A delivery for the order with the identifier {orderId} already exists");

    public static Error DriverAlreadyTried(Guid driverId) => Error.Problem(
        "Deliveries.DriverAlreadyTried",
        $"The driver with the identifier {driverId} has already been offered this delivery");

    public static Error InvalidTransition(DeliveryStatus from, DeliveryStatus to) => Error.Problem(
        "Deliveries.InvalidTransition",
        $"The delivery cannot move from status {from} to status {to}");
}
