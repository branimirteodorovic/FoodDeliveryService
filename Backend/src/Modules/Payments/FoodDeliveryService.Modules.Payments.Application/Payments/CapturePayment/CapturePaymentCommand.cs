using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Payments.Application.Payments.CapturePayment;

/// <summary>
/// Take the money that has been held since the order was placed — Feature 3.8 Milestone G, §1.3
/// step 5.
/// <para>
/// It names the order and nothing else. The amount is not a parameter: a capture takes the whole
/// authorized amount (partial capture is out of scope, §11.2), and passing a figure in would create
/// a second source of truth for a number the provider already holds.
/// </para>
/// <para>
/// Driven only by <c>OrderAcceptedIntegrationEvent</c> through the inbox, so it carries no validator
/// — the same rule as its sibling in <c>AuthorizePayment</c>.
/// </para>
/// </summary>
public sealed record CapturePaymentCommand(Guid OrderId) : ICommand;
