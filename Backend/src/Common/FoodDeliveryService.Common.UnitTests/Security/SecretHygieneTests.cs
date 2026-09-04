using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace FoodDeliveryService.Common.UnitTests.Security;

/// <summary>
/// Feature 3.7 Milestone B. One property that <c>gitleaks</c> cannot express.
/// <para>
/// The scanner answers "is this string a credential", which it decides by entropy — so it cannot
/// tell a blank <c>appsettings.json</c> from a populated one, and it is indifferent to *which* file
/// a value lives in. That distinction is the whole of this platform's secrets model:
/// <c>appsettings.json</c> ships in the container image and must be empty, while
/// <c>appsettings.Development.json</c> carries working local values and is never deployed.
/// </para>
/// <para>
/// A <see cref="TheoryAttribute"/> over the host directories rather than a list, so a tenth host is
/// covered the day it is added. See <c>docs/security.md</c> §3.
/// </para>
/// </summary>
public class SecretHygieneTests
{
    /// <summary>
    /// Names that read as a credential. Deliberately the same list the Kubernetes manifest gate
    /// uses (<c>deploy/k8s/scripts/policy-check.py</c>), so "what counts as a secret" has one
    /// definition across the two checks.
    /// </summary>
    private static readonly Regex CredentialKey = new(
        "password|secret|key|token|connectionstring",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Keys that match <see cref="CredentialKey"/> without being credentials. Each entry is here
    /// because a real key tripped the rule, and each carries the reason — an exemption without one
    /// is how a list like this stops meaning anything.
    /// </summary>
    private static readonly (string Path, string Reason)[] CredentialKeyExemptions =
    [
        ("RateLimiting:KeyPrefix", "the Redis key namespace for the edge limiter's counters, not a credential"),
        ("Authentication:TokenValidationParameters", "JWT *validation* parameters — public issuer/audience values")
    ];

    [Theory]
    [MemberData(nameof(HostSettingsFiles))]
    public void BaseAppSettings_ShipsEveryCredentialBlank(string relativePath)
    {
        string path = RepositoryPaths.Backend(relativePath.Split('/'));
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        List<string> populated = [];
        CollectPopulatedCredentials(document.RootElement, string.Empty, populated);

        // appsettings.json is the file that ships in the container image; appsettings.Development.json
        // is not deployed anywhere. A value here is a value in production, whatever the environment
        // is meant to supply.
        populated.Should().BeEmpty(
            $"{relativePath} is the deployed configuration — credentials come from the environment " +
            "(the platform-secrets Secret in Kubernetes, compose environment variables locally), so " +
            "every credential-shaped key here must be blank");
    }

    public static TheoryData<string> HostSettingsFiles()
    {
        var data = new TheoryData<string>();

        foreach (string directory in Directory.EnumerateDirectories(RepositoryPaths.Backend("src", "API")))
        {
            string settings = Path.Combine(directory, "appsettings.json");
            if (File.Exists(settings))
            {
                data.Add($"src/API/{Path.GetFileName(directory)}/appsettings.json");
            }
        }

        return data;
    }

    private static void CollectPopulatedCredentials(JsonElement element, string path, List<string> populated)
    {
        if (CredentialKeyExemptions.Any(exemption =>
                path.Equals(exemption.Path, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(exemption.Path + ":", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    CollectPopulatedCredentials(
                        property.Value,
                        path.Length == 0 ? property.Name : $"{path}:{property.Name}",
                        populated);
                }

                break;

            case JsonValueKind.String:
                string leaf = path[(path.LastIndexOf(':') + 1)..];
                if (CredentialKey.IsMatch(leaf) && element.GetString()?.Length > 0)
                {
                    populated.Add(path);
                }

                break;
        }
    }
}
