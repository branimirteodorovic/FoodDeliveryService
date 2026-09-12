using Microsoft.Extensions.Options;

namespace FoodDeliveryService.Modules.Payments.Infrastructure.Stripe;

/// <summary>
/// Shape checks on the Stripe credentials, run at host startup via <c>ValidateOnStart()</c>.
/// <para>
/// <b>Presence is deliberately not checked here.</b> That job belongs to
/// <c>AddRequiredConfiguration</c> in the host, which skips Development — and it has to skip it, or
/// a <c>docker-compose up</c> without Stripe user secrets, and every integration test that boots
/// this host with a fake gateway, would fail before serving a request. So this validator asserts
/// only the properties that must hold in <i>every</i> environment, including one where the values
/// are absent.
/// </para>
/// </summary>
internal sealed class StripeOptionsValidator : IValidateOptions<StripeOptions>
{
    public ValidateOptionsResult Validate(string? name, StripeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (!string.IsNullOrWhiteSpace(options.SecretKey))
        {
            // The one check worth failing a host over. This project is documented as test-mode only
            // (§0.3) and it is not built to take real money: it holds no PCI attestation, its
            // refunds are approved by a seeded local administrator, and its "customers" are Bogus
            // fixtures. A live key here would make every one of those statements false at once, and
            // the mistake is a plausible one — the two keys differ by four characters on a Stripe
            // dashboard that shows both.
            if (options.SecretKey.StartsWith("sk_live_", StringComparison.Ordinal) ||
                options.SecretKey.StartsWith("rk_live_", StringComparison.Ordinal))
            {
                failures.Add(
                    "Stripe:SecretKey is a LIVE key. This platform runs against Stripe test mode only " +
                    "(PAYMENTS_PHASE3_PLAN.md §0.3) and refuses to start with a key that can move real " +
                    "money. Use the sk_test_… key from the same dashboard.");
            }
            else if (!options.SecretKey.StartsWith("sk_test_", StringComparison.Ordinal) &&
                     !options.SecretKey.StartsWith("rk_test_", StringComparison.Ordinal))
            {
                // A publishable key (pk_test_…) pasted into the secret slot is the common version of
                // this. It fails at the first API call with a 401 that says nothing about which of
                // the two dashboard values was copied.
                failures.Add(
                    "Stripe:SecretKey does not look like a Stripe secret key — it must start with " +
                    "sk_test_ or rk_test_. A publishable key (pk_…) belongs in the browser, not here.");
            }
        }

        if (!string.IsNullOrWhiteSpace(options.WebhookSecret) &&
            !options.WebhookSecret.StartsWith("whsec_", StringComparison.Ordinal))
        {
            failures.Add(
                "Stripe:WebhookSecret must start with whsec_. `stripe listen --forward-to …` prints " +
                "the value for a local run; the dashboard holds the one for a deployed endpoint.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
