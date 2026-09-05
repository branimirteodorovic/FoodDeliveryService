using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using YamlDotNet.RepresentationModel;

namespace FoodDeliveryService.Common.UnitTests.Security;

/// <summary>
/// Feature 3.7 Milestone E, as assertions rather than as a paragraph in <c>docs/security.md</c>.
/// <para>
/// Every property here shares a shape: it is true today, nothing enforces it, and the day it stops
/// being true the platform keeps working — which is exactly what makes it dangerous. A signing-key
/// store that quietly reverts to the per-container file system, a client secret that falls back to
/// the committed development value, a seeded administrator password that fails the strength policy:
/// all three boot cleanly and pass every health probe.
/// </para>
/// <para>
/// The checks read the repository's own files, the same crude-and-deliberate approach
/// <see cref="SecurityHeaderCoverageTests"/> takes. The alternative is booting Duende against a real
/// PostgreSQL in order to observe a table.
/// </para>
/// </summary>
public class IdentityHardeningTests
{
    private const string PasswordLengthPattern = @"options\.Password\.RequiredLength\s*=\s*(?<length>\d+);";

    /// <summary>
    /// The character classes ASP.NET Core Identity requires by default. Identity's non-Development
    /// branch deliberately leaves all four alone and changes only the length, so these are the rules
    /// a seeded password has to satisfy.
    /// </summary>
    private static readonly (string Name, Func<char, bool> Predicate)[] RequiredCharacterClasses =
    [
        ("a digit", char.IsDigit),
        ("a lowercase letter", char.IsLower),
        ("an uppercase letter", char.IsUpper),
        ("a non-alphanumeric character", character => !char.IsLetterOrDigit(character))
    ];

    private static string IdentityProgram => ReadIdentityFile("Program.cs");

    [Fact]
    public void Identity_Should_PersistSigningKeysInAStoreEveryReplicaShares()
    {
        // Assert — with no operational store registered, Duende 8's automatic key management falls
        // back to a FileSystemKeyStore under the working directory. Nothing fails: the host starts,
        // issues tokens and serves a JWKS document. It is the *second* pod, or the first restart,
        // that produces the symptom — services intermittently rejecting valid tokens because the
        // discovery document they cached advertises the other key. docs/security.md §6.1.
        IdentityProgram.Should().Contain(
            ".AddOperationalStore(",
            "Duende's automatic key management needs a shared, durable ISigningKeyStore; without one " +
            "it writes ./keys per container and every restart invalidates every issued token");

        // The keys that store holds are encrypted with the ASP.NET Data Protection ring, so a shared
        // key store behind an unshared ring is no improvement at all. The same ring protects the
        // three-day invitation activation tokens.
        IdentityProgram.Should().Contain(
            ".PersistKeysToDbContext<ApplicationDbContext>()",
            "the Data Protection key ring must be shared too — it encrypts the signing keys and " +
            "protects the invitation activation tokens");
    }

    [Fact]
    public void Identity_Should_EnableLockoutOnTheTokenEndpoint()
    {
        // Assert — POST /connect/token does not pass through the Gateway, so the edge rate limiter
        // never sees it. Lockout is the only thing between a leaked email address and an unlimited
        // guessing loop against a live password-hashing endpoint.
        IdentityProgram.Should().Contain(
            "options.Lockout.MaxFailedAccessAttempts",
            "without lockout the token endpoint is an unrated password oracle (docs/security.md §6.3)");

        IdentityProgram.Should().Contain(
            "options.Lockout.AllowedForNewUsers = true",
            "lockout not enabled for new users covers nobody — every account here is a new user");
    }

    [Fact]
    public void IdentityConfig_Should_NotFallBackToTheCommittedDevelopmentSecret()
    {
        // Arrange — the value committed in appsettings.Development.json, which Config.cs used as its
        // `??` fallback until Milestone E.
        string developmentSecret = ReadDevelopmentClientSecret();

        // Assert — a fallback there silently undoes Milestone B. appsettings.json ships the key blank
        // precisely so a real environment must supply one, and the fallback handed that environment
        // the committed secret instead. A value nobody configured has to fail closed.
        ReadIdentityFile("Config.cs").Should().NotContain(
            developmentSecret,
            "Config.cs must not hard-code the development client secret as a fallback — a blank " +
            "configuration value has to fail closed, and outside Development it fails the boot");
    }

    [Fact]
    public void KubernetesManifest_Should_SeedAnAdministratorPasswordThatSatisfiesThePolicy()
    {
        // Arrange — the length floor is read out of Program.cs rather than repeated here, so raising
        // it stays a one-place change that this test then enforces against the manifest.
        int requiredLength = ReadNonDevelopmentPasswordLength();
        string seededPassword = ReadKubernetesSecret("AdminSeed__Password");

        // Assert — the guardrail that pays for this whole class. AdminSeeder logs the failure and
        // lets the host start, so a seed password failing the policy produces a healthy Identity with
        // no administrator in it. The first symptom is a login that cannot succeed, in a cluster
        // where nothing else can create an administrator.
        seededPassword.Length.Should().BeGreaterThanOrEqualTo(
            requiredLength,
            "Program.cs requires {0} characters outside Development, and a failed seed is silent",
            requiredLength);

        foreach ((string name, Func<char, bool> predicate) in RequiredCharacterClasses)
        {
            seededPassword.Should().Match<string>(
                password => password.Any(predicate),
                $"ASP.NET Identity's default policy applies outside Development and requires {name}");
        }
    }

    [Fact]
    public void KubernetesManifest_Should_NotMountAKeysVolume()
    {
        // Assert — the emptyDir this used to mount at /app/keys existed only because the file-system
        // key store needed a writable directory a non-root container could not create. Its presence
        // now would mean the operational store is not actually in use.
        string manifest = File.ReadAllText(
            RepositoryPaths.Backend("deploy", "k8s", "services", "identity.yaml"));

        manifest.Should().NotContain(
            "mountPath: /app/keys",
            "signing keys live in the identity database since Milestone E; a keys volume means the " +
            "host has fallen back to the per-pod FileSystemKeyStore");
    }

    private static string ReadIdentityFile(string fileName) =>
        File.ReadAllText(RepositoryPaths.Backend("src", "API", "FoodDeliveryService.Identity", fileName));

    private static int ReadNonDevelopmentPasswordLength()
    {
        MatchCollection matches = Regex.Matches(
            IdentityProgram,
            PasswordLengthPattern,
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        // Two assignments exist: the relaxed Development one first, then the real one. Asserting the
        // count is what keeps "the last match" from silently becoming the wrong branch.
        matches.Should().HaveCount(
            2,
            "Identity sets the password length in both the Development and the non-Development " +
            "branch; if that changed, this test is reading the wrong one");

        return int.Parse(matches[^1].Groups["length"].Value, CultureInfo.InvariantCulture);
    }

    private static string ReadDevelopmentClientSecret()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(RepositoryPaths.Backend(
                "src", "API", "FoodDeliveryService.Identity", "appsettings.Development.json")));

        return document.RootElement
            .GetProperty("Clients")
            .GetProperty("Confidential")
            .GetProperty("ClientSecret")
            .GetString()!;
    }

    private static string ReadKubernetesSecret(string key)
    {
        using var reader = new StringReader(
            File.ReadAllText(RepositoryPaths.Backend("deploy", "k8s", "base", "config.yaml")));

        var yaml = new YamlStream();
        yaml.Load(reader);

        // Parsed as YAML rather than sliced out of the text, for the same reason GatewayRouteTests
        // parses the gateway manifest: a restructured file has to fail this test, not silently match
        // nothing.
        foreach (YamlDocument document in yaml.Documents)
        {
            if (document.RootNode is YamlMappingNode root &&
                root.Children.TryGetValue(new YamlScalarNode("stringData"), out YamlNode? stringData) &&
                stringData is YamlMappingNode values &&
                values.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value))
            {
                return ((YamlScalarNode)value).Value!;
            }
        }

        throw new InvalidOperationException(
            $"No '{key}' entry in the platform-secrets Secret of deploy/k8s/base/config.yaml.");
    }
}
