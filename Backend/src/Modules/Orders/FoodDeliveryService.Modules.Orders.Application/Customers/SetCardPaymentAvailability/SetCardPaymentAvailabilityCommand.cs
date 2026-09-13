using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Orders.Application.Customers.SetCardPaymentAvailability;

/// <summary>
/// Projects Payments' <c>PaymentMethodAttached</c> / <c>PaymentMethodDetached</c> onto the one-flag
/// replica — Feature 3.8 Milestone D, §6.3. Both events reduce to this single command because the
/// only difference between them, from here, is the value of the flag.
/// <para>
/// Inbox-driven and unreachable from any endpoint, so it carries no validator by the same rule as
/// the other replica commands in this module.
/// </para>
/// </summary>
public sealed record SetCardPaymentAvailabilityCommand(
    Guid CustomerId,
    bool CanPayByCard,
    DateTime ChangedOnUtc) : ICommand;
