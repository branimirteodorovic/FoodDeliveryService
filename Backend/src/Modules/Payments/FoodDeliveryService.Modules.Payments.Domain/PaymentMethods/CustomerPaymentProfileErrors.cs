using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.Domain.PaymentMethods;

public static class CustomerPaymentProfileErrors
{
    /// <summary>
    /// The caller has no profile row yet — their <c>UserRegistered</c> event has not been consumed,
    /// or the Stripe customer creation it drives failed. A 404 rather than a lazily created profile:
    /// creating one here would put a Stripe API call on the read path and reopen the race §6.2
    /// exists to close.
    /// </summary>
    public static Error NotFound(Guid customerId) => Error.NotFound(
        "PaymentMethods.ProfileNotFound",
        $"No payment profile exists for the customer with the identifier {customerId}");

    public static Error PaymentMethodNotFound(Guid paymentMethodId) => Error.NotFound(
        "PaymentMethods.NotFound",
        $"No saved payment method with the identifier {paymentMethodId} was found");

    public static readonly Error PaymentMethodRequired = Error.Problem(
        "PaymentMethods.PaymentMethodRequired",
        "A payment method identifier is required");
}
