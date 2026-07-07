using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetMenuItem;

// Single-item snapshot used by the menu domain-event handlers to build full-snapshot integration
// events (hard rule #9) without loading the whole menu.
public sealed record GetMenuItemQuery(Guid MenuItemId) : IQuery<MenuItemSnapshotResponse>;
