using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetDelivery;

// A single delivery by its id — the assigned driver, the order's customer, or an admin may read it.
public sealed record GetDeliveryQuery(Guid DeliveryId) : IQuery<DeliveryResponse>;
