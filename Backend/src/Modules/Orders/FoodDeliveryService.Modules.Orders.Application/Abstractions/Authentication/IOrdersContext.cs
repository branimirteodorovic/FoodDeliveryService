namespace FoodDeliveryService.Modules.Orders.Application.Abstractions.Authentication;

public interface IOrdersContext
{
    /// <summary>
    /// The authenticated caller's user id (the `sub` claim) — the customer id for placement and
    /// cancellation, the manager id for ownership checks on status transitions.
    /// </summary>
    Guid UserId { get; }
}
