using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.Domain.Payments;

/// <summary>
/// An <see cref="Error"/> that also carries the bounded <see cref="PaymentFailureReason"/> behind it.
/// <para>
/// It exists because §5.3 says a decline is a <c>Result.Failure</c> while §8.4 says the resulting
/// integration event carries the reason code — and an <see cref="Error"/> is a code, a description
/// and a type, none of which is a place to put a reason a consumer can switch on. The alternatives
/// were encoding the reason into the error code and parsing it back out at the call site, or
/// widening every gateway method's success type to model a failure. A derived error is neither.
/// </para>
/// <para>
/// §8's handler recovers it with a type test:
/// <c>if (result.Error is PaymentDeclinedError declined) { … declined.Reason … }</c>. Everything
/// that only renders the error keeps working, because it is still an <see cref="Error"/> with a
/// stable code and <see cref="ErrorType.Problem"/>.
/// </para>
/// </summary>
public sealed record PaymentDeclinedError : Error
{
    /// <summary>
    /// One code for every decline — the reason is the payload, not the code. Private because
    /// <see cref="Error.Code"/> already exposes it; a <c>public const Code</c> here would hide the
    /// base property, which the build treats as an error.
    /// </summary>
    private const string DeclinedCode = "Payments.Declined";

    internal PaymentDeclinedError(string reason, string description)
        : base(DeclinedCode, description, ErrorType.Problem)
    {
        Reason = reason;
    }

    /// <summary>One of the <see cref="PaymentFailureReason"/> constants. Never Stripe's own text.</summary>
    public string Reason { get; }
}
