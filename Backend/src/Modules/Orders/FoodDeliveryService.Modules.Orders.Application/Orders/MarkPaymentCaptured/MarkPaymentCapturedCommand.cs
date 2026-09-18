using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.MarkPaymentCaptured;

/// <summary>
/// Projects <c>PaymentCapturedIntegrationEvent</c> onto the order's payment dimension — Feature 3.8
/// Milestone G, §1.3 step 5. Inbox-driven and unreachable from any endpoint, so no validator.
/// </summary>
public sealed record MarkPaymentCapturedCommand(Guid OrderId) : ICommand;
