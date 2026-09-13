using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Payments.Application.PaymentMethods.CreateSetupIntent;

/// <summary>
/// Starts card collection for the authenticated customer — §6.3.
/// <para>
/// It takes no parameters at all, and that is a security property rather than an oversight: the
/// customer is the JWT subject. A <c>CustomerId</c> in the body would let any caller start card
/// collection against somebody else's Stripe customer, and the resulting card would be charged for
/// that person's orders.
/// </para>
/// </summary>
public sealed record CreateSetupIntentCommand : ICommand<SetupIntentResponse>;
