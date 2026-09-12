using AwesomeAssertions;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Infrastructure.Stripe;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Stripe;

namespace FoodDeliveryService.Modules.Payments.UnitTests;

/// <summary>
/// Feature 3.8 Milestone C — the seam is actually wired, and it fails at the right moment.
/// <para>
/// Worth a test rather than a glance because both failure modes are quiet ones. A gateway that is
/// declared but never registered surfaces as a resolution failure at the first order, which is the
/// milestone after this one; and <c>ValidateOnStart()</c> is easy to write and easy to leave
/// unreachable, in which case a bad key is discovered by Stripe rather than by the host.
/// </para>
/// </summary>
public class StripeRegistrationTests
{
    private static ServiceProvider Build(
        string secretKey = "sk_test_not_a_real_key",
        string webhookSecret = "whsec_not_a_real_secret",
        string currency = "EUR")
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stripe:SecretKey"] = secretKey,
                ["Stripe:WebhookSecret"] = webhookSecret,
                ["Payments:Currency"] = currency
            })
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddStripe(configuration)
            .BuildServiceProvider();
    }

    [Fact]
    public void ThePaymentGateway_IsTheStripeOne()
    {
        using ServiceProvider provider = Build();

        provider.GetRequiredService<IPaymentGateway>().Should().BeOfType<StripePaymentGateway>();
    }

    [Fact]
    public void TheStripeClient_IsASingletonBuiltFromTheConfiguredKey()
    {
        using ServiceProvider provider = Build();

        var client = provider.GetRequiredService<IStripeClient>();

        client.ApiKey.Should().Be("sk_test_not_a_real_key");
        // One per process: StripeClient is thread-safe and owns the HTTP plumbing.
        provider.GetRequiredService<IStripeClient>().Should().BeSameAs(client);
    }

    [Fact]
    public void AMissingKey_FailsWithAMessageThatNamesIt()
    {
        // The local-development case: Program.cs's AddRequiredConfiguration skips Development, so
        // the host starts and this is where the omission surfaces. Being told which user secret to
        // set beats a 401 from Stripe.
        using ServiceProvider provider = Build(secretKey: "");

        Action resolve = () => provider.GetRequiredService<IStripeClient>();

        resolve.Should().Throw<InvalidOperationException>().WithMessage("*Stripe:SecretKey*");
    }

    [Fact]
    public void ALiveKey_BreaksStartupRatherThanTheFirstOrder()
    {
        // Proves ValidateOnStart() is reachable, not merely called: IStartupValidator is what
        // Program.cs invokes straight after Build().
        using ServiceProvider provider = Build(secretKey: "sk_live_not_a_real_key");

        Action validate = () => provider.GetRequiredService<IStartupValidator>().Validate();

        validate.Should().Throw<OptionsValidationException>().WithMessage("*LIVE key*");
    }

    [Fact]
    public void ANonsenseCurrency_BreaksStartupToo()
    {
        // Payments:Currency is a platform constant, so a typo in it is wrong for every order rather
        // than for one. Validated through Money.Create, so the boot check and the per-amount check
        // cannot disagree.
        using ServiceProvider provider = Build(currency: "EURO");

        Action validate = () => provider.GetRequiredService<IStartupValidator>().Validate();

        validate.Should().Throw<OptionsValidationException>().WithMessage("*Currency*");
    }

    [Fact]
    public void TheConfiguredCurrency_ReachesTheOptions()
    {
        using ServiceProvider provider = Build(currency: "eur");

        provider.GetRequiredService<IOptions<PaymentsOptions>>().Value.Currency.Should().Be("eur");
    }
}
