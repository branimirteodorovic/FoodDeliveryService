using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Authentication;

namespace FoodDeliveryService.Modules.Payments.Application.PaymentMethods.GetPaymentMethods;

/// <summary>
/// Dapper, like every read on this platform (hard rule #2).
/// <para>
/// The <c>WHERE</c> is on the caller's own id taken from the token, and
/// <c>stripe_payment_method_id IS NOT NULL</c> is what distinguishes "a profile with no card" — the
/// state every customer starts in — from "a card". A profile row always exists once the registration
/// event has been consumed, so without that predicate every customer would appear to have one blank
/// card.
/// </para>
/// </summary>
internal sealed class GetPaymentMethodsQueryHandler(
    IPaymentsContext paymentsContext,
    IDbConnectionFactory dbConnectionFactory)
    : IQueryHandler<GetPaymentMethodsQuery, IReadOnlyCollection<PaymentMethodResponse>>
{
    public async Task<Result<IReadOnlyCollection<PaymentMethodResponse>>> Handle(
        GetPaymentMethodsQuery request,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        // const, and every runtime value bound — SqlParameterisationTests is what proves no caller
        // input can be interpolated into a statement anywhere in this repository.
        const string sql =
            $"""
             SELECT
                 p.payment_method_id AS {nameof(PaymentMethodResponse.Id)},
                 p.brand AS {nameof(PaymentMethodResponse.Brand)},
                 p.last4 AS {nameof(PaymentMethodResponse.Last4)},
                 p.expiry_month AS {nameof(PaymentMethodResponse.ExpiryMonth)},
                 p.expiry_year AS {nameof(PaymentMethodResponse.ExpiryYear)},
                 p.attached_on_utc AS {nameof(PaymentMethodResponse.AttachedOnUtc)}
             FROM customer_payment_profiles p
             WHERE p.id = @CustomerId AND p.stripe_payment_method_id IS NOT NULL
             """;

        IEnumerable<PaymentMethodResponse> paymentMethods =
            await connection.QueryAsync<PaymentMethodResponse>(
                sql,
                new { CustomerId = paymentsContext.UserId });

        return paymentMethods.ToList();
    }
}
