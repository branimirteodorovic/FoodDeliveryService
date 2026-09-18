namespace FoodDeliveryService.Modules.Payments.Application.Payments.ReleasePayment;

/// <summary>
/// What ended the order, for the <c>trigger</c> tag on <c>payments.released</c> — Feature 3.8
/// Milestone I, §11.1.
/// <para>
/// Two constants rather than a free string for the reason every other bounded set in this module has
/// one: the value reaches a metric tag, and a tag whose values are written at the call site drifts
/// into <c>Rejected</c>, <c>rejected</c> and <c>order_rejected</c> as three series describing one
/// thing. It is deliberately not a reason code — <c>PaymentFailureReason</c> says why a card was not
/// charged, this says which of the two lifecycle endings released a hold that was.
/// </para>
/// </summary>
public static class PaymentReleaseTrigger
{
    /// <summary>The restaurant refused the order.</summary>
    public const string Rejected = "rejected";

    /// <summary>The customer, or the platform, cancelled it.</summary>
    public const string Cancelled = "cancelled";
}
