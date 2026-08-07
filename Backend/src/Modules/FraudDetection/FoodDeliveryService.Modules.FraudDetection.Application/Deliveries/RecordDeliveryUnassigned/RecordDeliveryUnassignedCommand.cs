using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.FraudDetection.Application.Deliveries.RecordDeliveryUnassigned;

/// <summary>
/// Every candidate driver within the radius was tried without an accept.
/// <para>
/// The plan puts an <c>Unassignments</c> counter on the driver projection, but
/// <c>DeliveryUnassignedIntegrationEvent</c> carries no driver — and correctly so: the event means
/// that <i>no</i> driver took the delivery, which is a property of the order, not of any one
/// person. It is therefore recorded on <c>OrderFact</c>. Attributing it per driver would need a
/// per-candidate event Delivery does not publish.
/// </para>
/// </summary>
public sealed record RecordDeliveryUnassignedCommand(
    Guid OrderId,
    DateTime UnassignedOnUtc) : ICommand;
