using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Notifications.Application.Notifications.SendOrderConfirmation;

/// <summary>
/// Resolves the customer from the local RecipientUser replica and sends the order-confirmation email
/// (the only email the system sends). A missing replica fails the command so the inbox retries — the
/// recipient address is never resolved by calling another service.
/// </summary>
public sealed record SendOrderConfirmationCommand(
    Guid CustomerId,
    Guid OrderId,
    decimal Subtotal) : ICommand;
