using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.ExpireDeliveryOffer;

// Sent by ProcessExpiredOffersJob for each delivery whose offer deadline has lapsed. Not exposed
// as an endpoint.
public sealed record ExpireDeliveryOfferCommand(Guid DeliveryId) : ICommand;
