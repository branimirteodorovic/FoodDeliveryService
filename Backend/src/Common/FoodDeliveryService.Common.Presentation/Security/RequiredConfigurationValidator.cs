using Microsoft.Extensions.Options;

namespace FoodDeliveryService.Common.Presentation.Security;

/// <summary>
/// Fails host startup when a required configuration key is blank outside Development — Feature 3.7
/// Milestone E. Registered by <see cref="RequiredConfigurationExtensions.AddRequiredConfiguration"/>
/// together with <c>ValidateOnStart()</c>, so the exception is thrown while the host is starting
/// rather than on the first request that needs the value.
/// <para>
/// The failure this exists for: <c>appsettings.json</c> ships every credential blank (docs/security.md
/// §3), so a deployment that forgets one gets an empty client secret and boots perfectly happily.
/// The symptom then surfaces hours later as a 401 from the token endpoint during a user registration,
/// with nothing in the logs pointing at configuration.
/// </para>
/// </summary>
internal sealed class RequiredConfigurationValidator : IValidateOptions<RequiredConfigurationOptions>
{
    public ValidateOptionsResult Validate(string? name, RequiredConfigurationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enforced)
        {
            return ValidateOptionsResult.Success;
        }

        List<string> failures = [.. options.Values
            .Where(entry => string.IsNullOrWhiteSpace(entry.Value))
            .Select(entry =>
                $"Configuration key '{entry.Key}' is required outside Development and is blank. " +
                $"Supply it from the environment (the platform-secrets Secret in Kubernetes, an " +
                $"environment variable in compose) — this host is running as " +
                $"'{options.EnvironmentName}'.")];

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
