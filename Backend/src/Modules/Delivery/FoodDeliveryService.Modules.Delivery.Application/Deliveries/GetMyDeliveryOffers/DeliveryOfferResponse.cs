namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetMyDeliveryOffers;

/// <summary>
/// One outstanding offer on the driver's offer screen — everything needed to decide, in one round
/// trip: where to collect, where it is going, and how long is left to answer.
/// <para>
/// There is no driver name or live position here (the driver <em>is</em> the caller) and no status
/// (every row is <c>Offered</c> by construction), which is why this is its own response rather than
/// a reuse of <c>DeliveryResponse</c>.
/// </para>
/// </summary>
public sealed record DeliveryOfferResponse(
    Guid Id,
    Guid OrderId,
    Guid RestaurantId,
    double PickupLatitude,
    double PickupLongitude,
    string DropoffStreet,
    string DropoffCity,
    string DropoffPostalCode,
    string DropoffCountry,
    string? DropoffNotes,
    double DropoffLatitude,
    double DropoffLongitude,
    DateTime OfferExpiresOnUtc,
    DateTime CreatedOnUtc);
