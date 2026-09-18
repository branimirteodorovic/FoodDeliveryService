using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Payments.Application.Payments.ReleasePayment;

/// <summary>
/// Give up a hold that will never be captured — Feature 3.8 Milestone G, §1.3 step 6. Driven by
/// <c>OrderRejectedIntegrationEvent</c> and <c>OrderCancelledIntegrationEvent</c>, which is why it
/// names the order rather than the reason: both endings mean the same thing to the money.
/// <para>
/// Inbox-driven only, so no validator.
/// </para>
/// </summary>
public sealed record ReleasePaymentCommand(Guid OrderId) : ICommand;
