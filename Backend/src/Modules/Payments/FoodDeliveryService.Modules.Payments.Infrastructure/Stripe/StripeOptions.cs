namespace FoodDeliveryService.Modules.Payments.Infrastructure.Stripe;

/// <summary>
/// The Stripe credentials, bound from the <c>Stripe</c> configuration section — §5.4.
/// <para>
/// <b>Internal, and it has to be.</b> The webhook endpoint (§7) will want
/// <see cref="WebhookSecret"/>, and it lives in the Presentation assembly, which Infrastructure
/// references rather than the other way round — so it cannot see this type at all. That is a
/// constraint on §7, not a reason to move this: signature verification belongs behind an
/// Application-layer abstraction implemented here, so that neither Stripe's SDK nor its API keys
/// reach an endpoint. The note is repeated in §7 of the plan, where it will be read.
/// </para>
/// <para>
/// Nothing populates these in <c>appsettings.json</c> — it ships them blank and
/// <c>SecretHygieneTests</c> keeps it that way. Development reads them from user secrets,
/// Kubernetes from the <c>platform-secrets</c> Secret.
/// </para>
/// </summary>
internal sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>
    /// The <c>sk_test_…</c> secret key. Never <c>sk_live_…</c> — see
    /// <see cref="StripeOptionsValidator"/> for why the host refuses to start on one.
    /// </summary>
    public string SecretKey { get; init; } = string.Empty;

    /// <summary>
    /// The <c>whsec_…</c> signing secret for the webhook endpoint. Printed by
    /// <c>stripe listen --forward-to …</c> for local runs, and different per environment — a
    /// production secret does not verify a locally forwarded event and vice versa.
    /// </summary>
    public string WebhookSecret { get; init; } = string.Empty;
}
