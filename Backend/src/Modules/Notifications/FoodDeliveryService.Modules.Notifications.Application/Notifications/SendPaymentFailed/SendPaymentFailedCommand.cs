using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Notifications.Application.Notifications.SendPaymentFailed;

/// <summary>
/// Tells the customer their card was not charged and their order is gone — Feature 3.8 Milestone H,
/// §10.4.
/// <para>
/// Driven by Payments' <c>PaymentAuthorizationFailed</c>, not by Orders' cancellation. Milestone F
/// deliberately kept <c>OrderPaymentFailedDomainEvent</c> internal to Orders (§8.7) precisely so
/// that this email would have one source: the Payments event carries the bounded reason, and two
/// messages about one fact would each carry less than this one does.
/// </para>
/// </summary>
/// <param name="Reason">
/// One of Payments' six bounded reason codes. The template turns it into a sentence — the provider's
/// own message never reaches a customer.
/// </param>
public sealed record SendPaymentFailedCommand(
    Guid CustomerId,
    Guid OrderId,
    string Reason) : ICommand;
