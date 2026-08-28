namespace FoodDeliveryService.Modules.Support.Domain.Tickets;

/// <summary>
/// What the customer is complaining about. Drives the analytics breakdown (Milestone G) and one
/// business rule: <see cref="OrderNotReceived"/> opens at <see cref="TicketPriority.High"/>.
/// </summary>
public enum TicketCategory
{
    OrderNotReceived = 0,
    ItemMissing = 1,
    FoodQuality = 2,
    DriverIssue = 3,
    PaymentIssue = 4,
    AppIssue = 5,
    Other = 6
}
