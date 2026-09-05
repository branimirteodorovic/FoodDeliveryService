using AwesomeAssertions;
using FoodDeliveryService.Common.Presentation.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FoodDeliveryService.Common.UnitTests.Security;

/// <summary>
/// Feature 3.7 Milestone E — the fail-fast half of Identity hardening.
/// <para>
/// The property under test is narrow and the failure it prevents is not: <c>appsettings.json</c>
/// ships every credential blank so a real environment must supply its own (docs/security.md §3), and
/// until this existed nothing checked that it did. A deployment missing the confidential client
/// secret booted cleanly, passed its health probes, and failed hours later as a 401 from
/// <c>api/users</c> in the middle of somebody's registration.
/// </para>
/// </summary>
public class RequiredConfigurationTests
{
    private const string SecretKey = "Clients:Confidential:ClientSecret";

    [Fact]
    public void Validation_Should_Fail_WhenARequiredKeyIsBlankOutsideDevelopment()
    {
        // Arrange
        IStartupValidator validator = BuildValidator("Kubernetes", (SecretKey, ""));

        // Act
        Action validate = validator.Validate;

        // Assert — the message has to name the key and the environment, or an operator reading a
        // crashed pod's last log line still does not know which value to go and set.
        validate.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().ContainSingle()
            .Which.Should().Contain(SecretKey).And.Contain("Kubernetes");
    }

    [Fact]
    public void Validation_Should_Fail_WhenARequiredKeyIsMissingEntirely()
    {
        // Arrange — a key absent from configuration is the same failure as one present and blank,
        // so the null and the empty string must not diverge here.
        IStartupValidator validator = BuildValidator("Production");

        // Act
        Action validate = validator.Validate;

        // Assert
        validate.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().ContainSingle().Which.Should().Contain(SecretKey);
    }

    [Fact]
    public void Validation_Should_Succeed_WhenTheKeyIsSupplied()
    {
        // Arrange
        IStartupValidator validator = BuildValidator("Kubernetes", (SecretKey, "a-real-secret"));

        // Act
        Action validate = validator.Validate;

        // Assert
        validate.Should().NotThrow();
    }

    [Fact]
    public void Validation_Should_Succeed_InDevelopment_EvenWhenBlank()
    {
        // Arrange — Development is the one environment whose committed values are deliberately weak
        // or absent, and where a host that refuses to boot is only an obstacle. Same reason the
        // Redis in-memory fallback is Development-only (docs/caching.md).
        IStartupValidator validator = BuildValidator(Environments.Development, (SecretKey, ""));

        // Act
        Action validate = validator.Validate;

        // Assert
        validate.Should().NotThrow();
    }

    [Fact]
    public void Validation_Should_ReportEveryMissingKey_NotOnlyTheFirst()
    {
        // Arrange — the keys accumulate across calls into one options instance on purpose: a boot
        // missing three values should cost one restart, not three.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Duende:TokenUrl"] = "http://identity/connect/token" })
            .Build();

        var services = new ServiceCollection();
        services.AddRequiredConfiguration(configuration, Environment("Kubernetes"), "Duende:AdminUrl", "Duende:TokenUrl");
        services.AddRequiredConfiguration(configuration, Environment("Kubernetes"), "Duende:ConfidentialClientSecret");

        IStartupValidator validator = services.BuildServiceProvider().GetRequiredService<IStartupValidator>();

        // Act
        Action validate = validator.Validate;

        // Assert — the populated key is absent from the report, the two blank ones are both in it.
        validate.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().HaveCount(2)
            .And.Contain(failure => failure.Contains("Duende:AdminUrl", StringComparison.Ordinal))
            .And.Contain(failure => failure.Contains("Duende:ConfidentialClientSecret", StringComparison.Ordinal));
    }

    private static IStartupValidator BuildValidator(
        string environmentName,
        params (string Key, string? Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(setting => setting.Key, setting => setting.Value))
            .Build();

        var services = new ServiceCollection();

        services.AddRequiredConfiguration(configuration, Environment(environmentName), SecretKey);

        return services.BuildServiceProvider().GetRequiredService<IStartupValidator>();
    }

    private static StubEnvironment Environment(string environmentName) =>
        new() { EnvironmentName = environmentName };

    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
