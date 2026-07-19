using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetDeliveries;

// The caller's delivery history — a driver's own by default; an administrator sees every delivery.
// Scope is resolved in the handler from the authenticated identity, not a query parameter.
public sealed record GetDeliveriesQuery(int Page, int PageSize)
    : IQuery<IReadOnlyCollection<DeliverySummaryResponse>>;
