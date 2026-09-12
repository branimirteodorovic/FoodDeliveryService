using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using global::Stripe;

namespace FoodDeliveryService.Modules.Payments.Infrastructure.Stripe;

/// <summary>
/// Wires the payment gateway seam — Feature 3.8 Milestone C. Called from
/// <c>PaymentsModule.AddInfrastructure</c>, which is where every other dependency of this module is
/// registered.
/// </summary>
internal static class StripeServiceCollectionExtensions
{
    internal static IServiceCollection AddStripe(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // The platform's own payment constants — the currency every amount is denominated in. Not a
        // secret, and not in the same options type as the Stripe credentials, because the handler
        // that reads it lives in the Application layer (see PaymentsOptions).
        services
            .AddOptions<PaymentsOptions>()
            .Bind(configuration.GetSection(PaymentsOptions.SectionName))
            .Validate(
                options => Money.Create(0m, options.Currency).IsSuccess,
                $"{PaymentsOptions.SectionName}:Currency must be an ISO 4217 alphabetic code, e.g. EUR.")
            .ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor
            .Singleton<IValidateOptions<StripeOptions>, StripeOptionsValidator>());

        services
            .AddOptions<StripeOptions>()
            .Bind(configuration.GetSection(StripeOptions.SectionName))
            .ValidateOnStart();

        // A named HttpClient rather than one built here: the factory recycles handlers (so DNS
        // changes are picked up and sockets are not exhausted), and it is what puts the Stripe call
        // on the HttpClient trace the OpenTelemetry instrumentation already collects. The handler
        // lifetime is left at the default; nothing about Stripe argues for a different one.
        services.AddHttpClient(StripeHttpClientName);

        // Singleton: StripeClient is thread-safe and holds the HTTP plumbing, so one per process is
        // right. Built in a factory rather than eagerly so that a host with no key configured —
        // local development without the user secret — still starts, and fails at the first API call
        // with a message that names the missing key instead of at Build() with a stack trace.
        services.AddSingleton<IStripeClient>(serviceProvider =>
        {
            StripeOptions options = serviceProvider.GetRequiredService<IOptions<StripeOptions>>().Value;

            if (string.IsNullOrWhiteSpace(options.SecretKey))
            {
                throw new InvalidOperationException(
                    "Stripe:SecretKey is not configured. Outside Development the host refuses to " +
                    "start without it (AddRequiredConfiguration in Program.cs); in Development it " +
                    "comes from user secrets: " +
                    "dotnet user-secrets set \"Stripe:SecretKey\" \"sk_test_...\" " +
                    "--project src/API/FoodDeliveryService.Payments.Api");
            }

            HttpClient httpClient = serviceProvider
                .GetRequiredService<IHttpClientFactory>()
                .CreateClient(StripeHttpClientName);

            return new StripeClient(
                options.SecretKey,
                httpClient: new SystemNetHttpClient(
                    httpClient,
                    // The SDK's own network retries sit *inside* this platform's error handling, and
                    // they are safe precisely because every mutating call carries an idempotency key
                    // (§1.4 rule 1). Left at the default of 2: a transient blip is resolved here in
                    // milliseconds, where the alternative is a StripeException thrown into an
                    // outbox job that does not retry.
                    maxNetworkRetries: SystemNetHttpClient.DefaultMaxNumberRetries,
                    // Off. Stripe's client telemetry reports request latencies back to Stripe on
                    // subsequent calls; the platform already measures its own outbound calls
                    // through OpenTelemetry, and a second, unasked-for egress channel is not worth
                    // the duplicate.
                    enableTelemetry: false));
        });

        services.AddScoped<IPaymentGateway, StripePaymentGateway>();

        return services;
    }

    /// <summary>
    /// The named client the Stripe SDK is handed. Named rather than typed because the consumer is
    /// <see cref="StripeClient"/>, which the SDK constructs, not a class of ours.
    /// </summary>
    private const string StripeHttpClientName = "stripe";
}
