using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Orders.Application.Restaurants.UpsertMenuItem;

// Builds the local MenuItem replica from the MenuItemAdded/MenuItemUpdated integration events —
// both carry the same full snapshot, so one upsert command serves both (inbox-driven, idempotent).
public sealed record UpsertMenuItemCommand(
    Guid MenuItemId,
    Guid RestaurantId,
    string Name,
    decimal Price,
    bool IsAvailable) : ICommand;
