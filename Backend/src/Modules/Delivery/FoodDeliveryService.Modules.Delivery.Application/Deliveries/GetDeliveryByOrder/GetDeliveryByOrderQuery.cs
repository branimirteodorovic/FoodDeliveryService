using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetDelivery;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetDeliveryByOrder;

// The customer's tracking lookup by order id — Feature 2.2 renders this. The order's customer, the
// assigned driver, or an admin may read it.
public sealed record GetDeliveryByOrderQuery(Guid OrderId) : IQuery<DeliveryResponse>;
