using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.AcceptDeliveryOffer;

// The offered driver (the authenticated caller) accepts the delivery.
public sealed record AcceptDeliveryOfferCommand(Guid DeliveryId) : ICommand;
