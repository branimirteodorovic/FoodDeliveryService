using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.MarkPaymentAuthorized;

/// <summary>
/// Projects <c>PaymentAuthorizedIntegrationEvent</c> onto the order's payment dimension — Feature
/// 3.8 Milestone F, §1.3 step 4. Lifting the guard on <c>Order.Accept()</c> is all it does.
/// <para>
/// Inbox-driven and unreachable from any endpoint, so it carries no validator, by the same rule as
/// the other replica commands in this module.
/// </para>
/// </summary>
public sealed record MarkPaymentAuthorizedCommand(Guid OrderId) : ICommand;
