using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.RejectDeliveryOffer;

// The offered driver (the authenticated caller) declines the delivery; the offer routine
// immediately moves on to the next-nearest candidate.
public sealed record RejectDeliveryOfferCommand(Guid DeliveryId) : ICommand;
