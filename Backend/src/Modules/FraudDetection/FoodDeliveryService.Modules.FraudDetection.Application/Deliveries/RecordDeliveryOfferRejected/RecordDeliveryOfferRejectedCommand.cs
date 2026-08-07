using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.FraudDetection.Application.Deliveries.RecordDeliveryOfferRejected;

// A driver actively declining an offer. Distinct from an offer that merely lapsed — Delivery does
// not publish those, so this counter measures refusals, not silence.
public sealed record RecordDeliveryOfferRejectedCommand(
    Guid DriverId,
    DateTime RejectedOnUtc) : ICommand;
