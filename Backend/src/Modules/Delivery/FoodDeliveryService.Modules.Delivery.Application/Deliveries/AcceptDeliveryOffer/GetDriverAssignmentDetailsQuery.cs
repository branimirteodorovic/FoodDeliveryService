using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.AcceptDeliveryOffer;

// Internal enrichment read for the DriverAssigned publish path (outbox context — no authenticated
// caller, so unlike GetDriverQuery it carries no self-or-admin check).
public sealed record GetDriverAssignmentDetailsQuery(Guid DriverId) : IQuery<DriverAssignmentDetailsResponse>;

public sealed record DriverAssignmentDetailsResponse(
    Guid Id,
    string FirstName,
    string LastName,
    VehicleType VehicleType);
