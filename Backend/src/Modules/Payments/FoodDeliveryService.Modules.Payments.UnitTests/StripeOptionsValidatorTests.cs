using AwesomeAssertions;
using FoodDeliveryService.Modules.Payments.Infrastructure.Stripe;
using Microsoft.Extensions.Options;

namespace FoodDeliveryService.Modules.Payments.UnitTests;

/// <summary>
/// Feature 3.8 Milestone C, §5.4 — the half of the configuration fail-fast that runs in every
/// environment.
/// <para>
/// The division of labour is the thing to understand here, because it is not obvious.
/// <c>AddRequiredConfiguration</c> in the host asserts the keys are <i>present</i> and skips
/// Development, so a compose stack and an integration-test host start without Stripe secrets. This
/// validator asserts the keys are the <i>right shape</i> and never skips anything — which is what
/// makes the live-key refusal unconditional.
/// </para>
/// </summary>
public class StripeOptionsValidatorTests
{
    // Every literal key below is deliberately shaped so that no run of 16 alphanumeric characters
    // follows its prefix. That is what keeps them out of SecretHygieneTests' Stripe scan and out of
    // gitleaks — a realistic-looking fixture key would fail the very check this milestone added.
    private static ValidateOptionsResult Validate(string secretKey = "", string webhookSecret = "") =>
        new StripeOptionsValidator().Validate(
            name: null,
            new StripeOptions { SecretKey = secretKey, WebhookSecret = webhookSecret });

    [Fact]
    public void BlankValues_Pass()
    {
        // Development, where the keys legitimately come from user secrets that a given developer may
        // not have set. Absence is AddRequiredConfiguration's business, not this validator's — and
        // if this rejected blanks, no integration test could ever boot the host.
        Validate().Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("sk_live_not_a_real_key")]
    [InlineData("rk_live_not_a_real_key")]
    public void ALiveKey_FailsInEveryEnvironment(string secretKey)
    {
        // The README, docs/security.md and PAYMENTS_PHASE3_PLAN.md §0.3 all state that this platform
        // cannot move real money. This is the line that makes the statement true rather than
        // aspirational: on a Stripe dashboard the test and live keys differ by four characters and
        // sit next to each other.
        ValidateOptionsResult result = Validate(secretKey);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("LIVE key");
    }

    [Theory]
    [InlineData("sk_test_not_a_real_key")]
    [InlineData("rk_test_not_a_real_key")]
    public void ATestKey_Passes(string secretKey)
    {
        Validate(secretKey).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void APublishableKey_IsRejectedWithTheReasonSpelledOut()
    {
        // The common version of this mistake. Left to Stripe it surfaces as a 401 at the first
        // charge, saying nothing about which of the two values on the dashboard was copied.
        ValidateOptionsResult result = Validate("pk_test_not_a_real_key");

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("publishable key");
    }

    [Fact]
    public void AWebhookSecretWithTheWrongPrefix_Fails()
    {
        Validate(webhookSecret: "not-a-signing-secret").Failed.Should().BeTrue();
    }

    [Fact]
    public void AWebhookSecretWithTheRightPrefix_Passes()
    {
        Validate(webhookSecret: "whsec_kind_local_no_stripe_account").Succeeded.Should().BeTrue();
    }

    [Fact]
    public void ThePlaceholdersShippedInTheKubernetesSecret_Pass()
    {
        // deploy/k8s/base/config.yaml ships these so the KinD cluster can satisfy the presence check
        // without a Stripe account behind it. If a future tightening of the shape rules rejects them,
        // the Payments pod stops starting in the cluster smoke test — this asserts the two stay in
        // agreement.
        Validate("sk_test_kind_local_no_stripe_account", "whsec_kind_local_no_stripe_account")
            .Succeeded.Should().BeTrue();
    }
}
