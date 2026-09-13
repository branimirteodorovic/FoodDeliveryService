using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Payments.Application.PaymentMethods.GetPaymentMethods;

/// <summary>
/// The caller's own saved cards — §6.3. No customer parameter and no administrator bypass: this
/// endpoint carries <c>payment-methods:manage</c>, which is the code for managing <em>your own</em>
/// cards. An administrator who needs to see what a customer can pay with reads the payment, not the
/// card.
/// <para>
/// Unpaged and uncached. At most one card exists per customer today (§6.1), so a page size would be
/// a parameter with one legal value, and a cache would hold a row that changes exactly when the
/// customer is looking at it.
/// </para>
/// </summary>
public sealed record GetPaymentMethodsQuery : IQuery<IReadOnlyCollection<PaymentMethodResponse>>;
