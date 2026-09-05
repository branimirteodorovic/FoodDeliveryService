using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace FoodDeliveryService.Common.UnitTests.Security;

/// <summary>
/// Feature 3.7 Milestone F §7.3. Reads go through Dapper with named parameters and writes go through
/// EF Core, so the platform has no SQL injection today. This suite is here for the regression, not
/// the audit: the one way to reintroduce it is to build a statement out of a runtime value, and
/// nothing in the build would have objected.
/// <para>
/// The check it makes is stronger than the source grep the plan sketched, because of a property the
/// codebase already had and nobody had written down: <b>every SQL literal is declared
/// <c>const</c></b>. That matters more than it looks. All of this platform's SQL is written as an
/// interpolated raw string (<c>$"""…"""</c>) so that column aliases can be <c>nameof</c>-ed against
/// the response record, and an interpolated string that is <c>const</c> can only interpolate other
/// compile-time constants — the C# compiler refuses to put a runtime value in one. So "is it
/// <c>const</c>?" is not a heuristic about the text: it is the compiler proving that no caller's
/// input can reach the statement.
/// </para>
/// <para>
/// It is still a file scan, and a crude one, in the shape of <c>ObservabilityAssetTests</c>. It
/// catches exactly the regression it exists for.
/// </para>
/// </summary>
public partial class SqlParameterisationTests
{
    /// <summary>
    /// Where product SQL is allowed to live: the Application layer (Dapper reads) and the
    /// Infrastructure layer (the outbox/inbox jobs). A statement anywhere else — an endpoint, a
    /// domain entity — is its own problem and this suite would not find it, so
    /// <see cref="SqlLiterals_Should_LiveOnlyInTheseLayers"/> fails if one appears.
    /// </summary>
    private static readonly string[] SqlLayers = ["Application", "Infrastructure"];

    private static readonly string[] SqlKeywords =
        ["SELECT ", "INSERT INTO", "UPDATE ", "DELETE FROM"];

    [Fact]
    public void EverySqlLiteral_Should_BeCompileTimeConstant()
    {
        // Arrange
        var offenders = new List<string>();

        foreach (SourceFile file in ProductSources())
        {
            foreach (Match match in SqlLiteralDeclaration().Matches(file.Text))
            {
                if (!ContainsSql(file.Text, match.Index))
                {
                    continue;
                }

                if (!match.Value.Contains("const ", StringComparison.Ordinal))
                {
                    offenders.Add($"{file.RelativePath}: {match.Value.Trim()}");
                }
            }
        }

        // Assert
        offenders.Should().BeEmpty(
            "a SQL literal that is not const can interpolate a runtime value, which is the one way " +
            "back to a concatenated statement. Declare it const and pass the values as Dapper " +
            "parameters (@Name).");
    }

    [Fact]
    public void NoSqlLiteral_Should_BeBuiltByConcatenation()
    {
        // Arrange — the other shape the same mistake takes. `+ " WHERE …"`, string.Format and
        // string.Concat all sidestep the const rule above by never producing a single literal.
        var offenders = new List<string>();

        foreach (SourceFile file in ProductSources())
        {
            foreach (Match match in SqlConcatenation().Matches(file.Text))
            {
                offenders.Add($"{file.RelativePath}: {match.Value.Trim()}");
            }
        }

        // Assert
        offenders.Should().BeEmpty("SQL is never assembled from fragments — it is one const literal");
    }

    [Fact]
    public void SqlLiterals_Should_LiveOnlyInTheseLayers()
    {
        // Arrange — a vacuity guard and a layering check in one. If this ever finds nothing at all,
        // the scan below has stopped reading the sources and every assertion here is empty.
        var layers = new HashSet<string>(StringComparer.Ordinal);
        var strays = new List<string>();

        foreach (SourceFile file in ProductSources())
        {
            if (!SqlKeywords.Any(keyword => file.Text.Contains(keyword, StringComparison.Ordinal)))
            {
                continue;
            }

            if (SqlLayers.Contains(file.Layer))
            {
                layers.Add(file.Layer);
            }
            else
            {
                strays.Add(file.RelativePath);
            }
        }

        // Assert
        layers.Should().BeEquivalentTo(SqlLayers, "both layers still hold SQL and are still scanned");
        strays.Should().BeEmpty(
            "reads belong in an Application query handler and writes belong to EF Core — SQL in any " +
            "other layer is outside everything this suite and the repository pattern guarantee");
    }

    /// <summary>
    /// Every <c>.cs</c> file under <c>src/Modules</c> that is product code — test projects are
    /// excluded (they legitimately build SQL to arrange state), as are the generated EF Core
    /// migrations, which are compiler output in all but name.
    /// </summary>
    private static IEnumerable<SourceFile> ProductSources()
    {
        string modules = RepositoryPaths.Backend("src", "Modules");

        foreach (string path in Directory.EnumerateFiles(modules, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(modules, path).Replace('\\', '/');

            if (relative.Contains("/bin/", StringComparison.Ordinal) ||
                relative.Contains("/obj/", StringComparison.Ordinal) ||
                relative.Contains("/Migrations/", StringComparison.Ordinal) ||
                relative.Contains("Tests/", StringComparison.Ordinal))
            {
                continue;
            }

            // src/Modules/{Module}/FoodDeliveryService.Modules.{Module}.{Layer}/…
            string[] segments = relative.Split('/');

            if (segments.Length < 3)
            {
                continue;
            }

            yield return new SourceFile(
                relative,
                segments[1].Split('.')[^1],
                File.ReadAllText(path));
        }
    }

    /// <summary>
    /// True when the raw string literal opened at <paramref name="index"/> is SQL rather than, say,
    /// a JSON fixture. Only the opening of the literal is inspected — enough to see the verb.
    /// </summary>
    private static bool ContainsSql(string text, int index)
    {
        string window = text[index..Math.Min(text.Length, index + 400)];

        return SqlKeywords.Any(keyword => window.Contains(keyword, StringComparison.Ordinal));
    }

    /// <summary>
    /// A string declaration whose value opens on the next line as a raw string literal — the shape
    /// every statement in this codebase is written in. The <c>const</c> keyword is captured (or not)
    /// so the assertion can look for it.
    /// </summary>
    [GeneratedRegex(@"(?:const\s+)?string\s+\w+\s*=\s*\r?\n\s*\$?""""""", RegexOptions.Multiline)]
    private static partial Regex SqlLiteralDeclaration();

    /// <summary>
    /// Deliberately case-SENSITIVE, on uppercase keywords only. The first draft was case-insensitive
    /// and its first catch was the phrase "live update" inside an email template — prose contains
    /// these words constantly, SQL in this codebase is always written in capitals, and a check that
    /// cries wolf gets deleted rather than fixed.
    /// </summary>
    [GeneratedRegex(@"(?:\+\s*""[^""]*(?:SELECT |INSERT INTO |UPDATE |DELETE FROM | WHERE | FROM )|" +
                    @"string\.(?:Format|Concat|Join)\([^)]*(?:SELECT |INSERT INTO |UPDATE |DELETE FROM | WHERE ))")]
    private static partial Regex SqlConcatenation();

    private sealed record SourceFile(string RelativePath, string Layer, string Text);
}
