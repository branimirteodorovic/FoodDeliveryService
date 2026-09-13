using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.Domain.PaymentMethods;

/// <summary>
/// A customer's standing relationship with the payment provider — Feature 3.8 Milestone D, §6.1.
/// One row per registered user, created from <c>UserRegisteredIntegrationEvent</c> (§6.2) before
/// there is any card on it, and carrying at most one saved card thereafter.
/// <para>
/// <b>What is stored here is the outer limit of what may be stored anywhere (§0.5).</b> The Stripe
/// identifiers, plus the brand, last four digits and expiry <em>for display</em>. No PAN, no CVC, no
/// full expiry-bearing card object: those never reach this backend at all, because the browser
/// confirms the card directly against Stripe. An endpoint or a column that would accept a card
/// number is a defect, not a feature — it silently moves this platform from SAQ-A to SAQ-D.
/// </para>
/// <para>
/// <b>One card, replaced rather than accumulated.</b> Attaching a second card supersedes the first,
/// which keeps "which card is this order charged to?" from being a question the order has to answer.
/// The API shape is still a collection (§6.3) so that a later multi-card change is a change to this
/// aggregate rather than to every consumer of the endpoint.
/// </para>
/// </summary>
public sealed class CustomerPaymentProfile : Entity
{
    private CustomerPaymentProfile()
    {
    }

    /// <summary>
    /// The customer, which is the Users service's UserId. The natural key: a customer has exactly
    /// one relationship with the payment provider, so a surrogate would only add a lookup.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>The Stripe customer object (<c>cus_…</c>) every later call is made against.</summary>
    public string StripeCustomerId { get; private set; }

    /// <summary>
    /// The public identifier of the saved card — the one that appears in a URL and in a response
    /// body. Deliberately <em>not</em> the <c>pm_…</c> id: a Stripe identifier in a route is a
    /// provider detail leaking into this platform's API, and it would have to be re-issued the day
    /// the provider changes. Null while no card is saved.
    /// </summary>
    public Guid? PaymentMethodId { get; private set; }

    /// <summary>
    /// The Stripe payment method (<c>pm_…</c>). Never published on an integration event and never
    /// returned from an endpoint — no other service, and no client, has any business holding it.
    /// </summary>
    public string? StripePaymentMethodId { get; private set; }

    /// <summary>Display only: "visa", "mastercard". Never used to decide anything.</summary>
    public string? Brand { get; private set; }

    /// <summary>Display only: the last four digits, which is what a customer recognises a card by.</summary>
    public string? Last4 { get; private set; }

    public int? ExpiryMonth { get; private set; }

    public int? ExpiryYear { get; private set; }

    public DateTime? AttachedOnUtc { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    /// <summary>
    /// True when this customer can be charged off-session. It is the single fact Orders replicates
    /// (§6.3) — everything else on this aggregate is either a provider identifier or a display
    /// string, and neither is any other service's business.
    /// </summary>
    public bool HasPaymentMethod => StripePaymentMethodId is not null;

    /// <summary>
    /// Created when the user registers, not when they first reach for a card. A Stripe customer with
    /// no payment method costs nothing, and creating it eagerly removes a lazy-creation race from
    /// the SetupIntent path — two browser tabs opening the card form at the same instant would
    /// otherwise create two customer objects for one person (§6.2).
    /// </summary>
    public static CustomerPaymentProfile Create(Guid customerId, string stripeCustomerId, DateTime utcNow)
    {
        return new CustomerPaymentProfile
        {
            Id = customerId,
            StripeCustomerId = stripeCustomerId,
            CreatedOnUtc = utcNow
        };
    }

    /// <summary>
    /// Records a card that is already attached at Stripe. The attachment itself happened before this
    /// call — either in the browser against a SetupIntent (§7) or, until there is a browser, through
    /// the Development-only endpoint in §6.4 — so this method never fails on the provider's behalf.
    /// <para>
    /// Attaching the same <c>pm_…</c> twice is a no-op rather than a second event: the confirmation
    /// webhook is redelivered on any non-2xx (§7.4), and a redelivery that republished
    /// <c>PaymentMethodAttached</c> would have every consumer re-project a change that did not
    /// happen.
    /// </para>
    /// </summary>
    public Result Attach(
        Guid paymentMethodId,
        string stripePaymentMethodId,
        string? brand,
        string? last4,
        int? expiryMonth,
        int? expiryYear,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(stripePaymentMethodId))
        {
            return Result.Failure(CustomerPaymentProfileErrors.PaymentMethodRequired);
        }

        if (string.Equals(StripePaymentMethodId, stripePaymentMethodId, StringComparison.Ordinal))
        {
            return Result.Success();
        }

        PaymentMethodId = paymentMethodId;
        StripePaymentMethodId = stripePaymentMethodId;
        Brand = brand;
        Last4 = last4;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        AttachedOnUtc = utcNow;

        Raise(new PaymentMethodAttachedDomainEvent(
            Id,
            paymentMethodId,
            brand,
            last4,
            expiryMonth,
            expiryYear,
            utcNow));

        return Result.Success();
    }

    /// <summary>
    /// Forgets the saved card. The caller detaches it at Stripe first and then calls this: the other
    /// order would leave a row claiming a card that the provider has already released, which is the
    /// version of the inconsistency a customer notices.
    /// <para>
    /// The id is checked rather than ignored. Only one card can be saved, so "delete the card" would
    /// work without it — but a stale browser tab holding the id of a card replaced five minutes ago
    /// would then delete the new one.
    /// </para>
    /// </summary>
    public Result Detach(Guid paymentMethodId, DateTime utcNow)
    {
        if (!HasPaymentMethod || PaymentMethodId != paymentMethodId)
        {
            return Result.Failure(CustomerPaymentProfileErrors.PaymentMethodNotFound(paymentMethodId));
        }

        PaymentMethodId = null;
        StripePaymentMethodId = null;
        Brand = null;
        Last4 = null;
        ExpiryMonth = null;
        ExpiryYear = null;
        AttachedOnUtc = null;

        Raise(new PaymentMethodDetachedDomainEvent(Id, paymentMethodId, utcNow));

        return Result.Success();
    }
}
