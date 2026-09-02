using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Support.Application.Orders.UpsertOrderSnapshot;

/// <summary>
/// Projects an <c>OrderPlaced</c> integration event into Support's local order replica.
/// <para>
/// <paramref name="OccurredOnUtc"/> is the event's own timestamp, not the clock: the projection has
/// to be able to tell a late redelivery from a newer fact, and the moment the row happens to be
/// written says nothing about that.
/// </para>
/// </summary>
public sealed record UpsertOrderSnapshotCommand(
    Guid OrderId,
    Guid CustomerId,
    Guid RestaurantId,
    decimal Subtotal,
    DateTime PlacedOnUtc,
    DateTime OccurredOnUtc) : ICommand;
