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

    /// <summary>
    /// A live Stripe key, a test Stripe key, or a webhook signing secret with a real body — Feature
    /// 3.8 Milestone C, §5.4.
    /// <para>
    /// The prefix alone is not enough to match, and that is deliberate: the placeholders in
    /// <c>deploy/k8s/base/config.yaml</c>, the prose in the plan and the literals in
    /// <c>StripeOptionsValidator</c> all name these prefixes legitimately. A real key carries at
    /// least 16 unbroken alphanumeric characters after its prefix; a placeholder that spells out why
    /// it is a placeholder cannot.
    /// </para>
    /// </summary>
    private static readonly Regex StripeCredential = new(
        @"\b(?:(?:sk|rk)_(?:live|test)|whsec)_[A-Za-z0-9]{16,}\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Directory names whose contents are build output, containers or test artefacts rather than
    /// tracked source. Walking into them turns a sub-second test into a minute-long one.
    /// </summary>
    private static readonly string[] NotTrackedSource =
        ["bin", "obj", "node_modules", ".vs", ".containers", "TestResults", "results"];

    /// <summary>
    /// The text formats a credential could plausibly be pasted into. Binary and image files are
    /// skipped because reading them as text is slow and finds nothing.
    /// </summary>
    private static readonly string[] ScannedExtensions =
    [
        ".cs", ".json", ".yml", ".yaml", ".sh", ".ps1", ".md",
        ".env", ".sql", ".props", ".csproj", ".txt", ".js", ".mjs"
    ];

    /// <summary>
    /// gitleaks already carries a Stripe rule and CI runs it, so why this too: gitleaks scans the
    /// tracked tree at push time, this fails the build on a developer's machine before the commit
    /// exists. For a credential that a third party revokes on your behalf — Stripe scans public
    /// repositories and kills committed keys, usually before you have noticed — the earlier gate is
    /// the one that saves the embarrassment. <c>whsec_</c> is not in the gitleaks default rule set
    /// at all; <c>.gitleaks.toml</c> adds it for the push-time half.
    /// </summary>
    [Fact]
    public void NoStripeCredential_IsCommittedAnywhereUnderBackend()
    {
        string backend = RepositoryPaths.Backend();

        List<string> offenders = [];

        foreach (string path in Directory.EnumerateFiles(backend, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(backend, path);

            if (relative.Split(Path.DirectorySeparatorChar).Any(NotTrackedSource.Contains) ||
                !ScannedExtensions.Contains(Path.GetExtension(path)))
            {
                continue;
            }

            if (StripeCredential.IsMatch(File.ReadAllText(path)))
            {
                offenders.Add(relative);
            }
        }

        offenders.Should().BeEmpty(
            "a Stripe key belongs in user secrets locally and in the platform-secrets Secret in " +
            "Kubernetes — never in a file. This platform runs in Stripe test mode only, so the worst " +
            "case is a revoked test key rather than a stolen card, but a live key committed to a " +
            "public repository is the single worst outcome this feature has available");
    }

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
