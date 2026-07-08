namespace FoodDeliveryService.Modules.Orders.Domain.Orders;

// Input to Order.Place: one requested line already priced by the placement handler from the local
// MenuItem replica (name and unit price are never client-supplied). The aggregate turns each line
// into an OrderItem and computes the totals itself.
public sealed record OrderLine(
    Guid MenuItemId,
    string Name,
    decimal UnitPrice,
    int Quantity);
