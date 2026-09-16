using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.FailOrderPayment;

/// <summary>
/// Projects <c>PaymentAuthorizationFailedIntegrationEvent</c> onto the order — Feature 3.8
/// Milestone F, §8.3. The order is cancelled, and it is cancelled with its own distinct domain event
/// rather than through the customer's cancellation path.
/// <para>
/// Inbox-driven and unreachable from any endpoint, so it carries no validator.
/// <see cref="Reason"/> is a bounded code Payments published, not free text a caller chose.
/// </para>
/// </summary>
public sealed record FailOrderPaymentCommand(
    Guid OrderId,
    string Reason,
    DateTime FailedOnUtc) : ICommand;
