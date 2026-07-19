using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Locations;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetDelivery;

// The delivery row as read from Postgres (Dapper), including the assigned driver's name via a LEFT
// JOIN onto drivers. The live driver position is merged in from the location store afterwards — it
// is not a database column. Shared by GetDelivery and GetDeliveryByOrder.
internal sealed record DeliveryDetailRow(
    Guid Id,
    Guid OrderId,
    Guid RestaurantId,
    Guid CustomerId,
    DeliveryStatus Status,
    Guid? DriverId,
    string? DriverFirstName,
    string? DriverLastName,
    double PickupLatitude,
    double PickupLongitude,
    string DropoffStreet,
    string DropoffCity,
    string DropoffPostalCode,
    string DropoffCountry,
    string? DropoffNotes,
    double DropoffLatitude,
    double DropoffLongitude,
    DateTime? OfferExpiresOnUtc,
    DateTime? AssignedOnUtc,
    DateTime? PickedUpOnUtc,
    DateTime? DeliveredOnUtc,
    DateTime CreatedOnUtc)
{
    // Column list shared by both detail queries — only the WHERE clause differs.
    internal const string SelectSql =
        $"""
         SELECT
             d.id AS {nameof(Id)},
             d.order_id AS {nameof(OrderId)},
             d.restaurant_id AS {nameof(RestaurantId)},
             d.customer_id AS {nameof(CustomerId)},
             d.status AS {nameof(Status)},
             d.driver_id AS {nameof(DriverId)},
             dr.first_name AS {nameof(DriverFirstName)},
             dr.last_name AS {nameof(DriverLastName)},
             d.pickup_latitude AS {nameof(PickupLatitude)},
             d.pickup_longitude AS {nameof(PickupLongitude)},
             d.dropoff_street AS {nameof(DropoffStreet)},
             d.dropoff_city AS {nameof(DropoffCity)},
             d.dropoff_postal_code AS {nameof(DropoffPostalCode)},
             d.dropoff_country AS {nameof(DropoffCountry)},
             d.dropoff_notes AS {nameof(DropoffNotes)},
             d.dropoff_latitude AS {nameof(DropoffLatitude)},
             d.dropoff_longitude AS {nameof(DropoffLongitude)},
             d.offer_expires_on_utc AS {nameof(OfferExpiresOnUtc)},
             d.assigned_on_utc AS {nameof(AssignedOnUtc)},
             d.picked_up_on_utc AS {nameof(PickedUpOnUtc)},
             d.delivered_on_utc AS {nameof(DeliveredOnUtc)},
             d.created_on_utc AS {nameof(CreatedOnUtc)}
         FROM deliveries d
         LEFT JOIN drivers dr ON dr.id = d.driver_id
         """;

    internal DeliveryResponse ToResponse(DriverLocation? currentLocation) =>
        new(
            Id,
            OrderId,
            RestaurantId,
            CustomerId,
            Status,
            DriverId,
            DriverFirstName,
            DriverLastName,
            PickupLatitude,
            PickupLongitude,
            DropoffStreet,
            DropoffCity,
            DropoffPostalCode,
            DropoffCountry,
            DropoffNotes,
            DropoffLatitude,
            DropoffLongitude,
            OfferExpiresOnUtc,
            AssignedOnUtc,
            PickedUpOnUtc,
            DeliveredOnUtc,
            CreatedOnUtc,
            currentLocation?.Location.Latitude,
            currentLocation?.Location.Longitude,
            currentLocation?.RecordedOnUtc);
}
