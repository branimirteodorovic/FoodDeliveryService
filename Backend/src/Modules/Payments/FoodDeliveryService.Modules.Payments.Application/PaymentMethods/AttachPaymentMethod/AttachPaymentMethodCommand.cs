using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Payments.Application.PaymentMethods.AttachPaymentMethod;

/// <summary>
/// Records a card against a customer — §6.3.
/// <para>
/// <b><see cref="CustomerId"/> is never read from a request body.</b> The Development-only endpoint
/// in §6.4 fills it from the authenticated caller, and the webhook in §7 will fill it from the
/// SetupIntent's own customer. A body field would let anyone attach a card to somebody else's
/// account — and that card would then be charged for that person's orders.
/// </para>
/// </summary>
public sealed record AttachPaymentMethodCommand(
    Guid CustomerId,
    string StripePaymentMethodId) : ICommand<Guid>;
