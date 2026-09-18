using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.MarkPaymentReleased;

/// <summary>
/// Projects <c>PaymentReleasedIntegrationEvent</c> onto the order's payment dimension — Feature 3.8
/// Milestone G, §1.3 step 6. Inbox-driven and unreachable from any endpoint, so no validator.
/// </summary>
public sealed record MarkPaymentReleasedCommand(Guid OrderId) : ICommand;
