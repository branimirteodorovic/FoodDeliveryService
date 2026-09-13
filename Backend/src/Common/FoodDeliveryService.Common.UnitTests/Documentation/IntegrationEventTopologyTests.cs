using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace FoodDeliveryService.Common.UnitTests.Documentation;

/// <summary>
/// Feature 3.7 Milestone I. The README's C3 event-topology section, asserted against the code it
/// claims to describe.
/// <para>
/// A diagram is the one kind of documentation nobody notices going stale: it keeps rendering, it
/// keeps looking authoritative, and the only signal that a service started consuming an event two
/// months ago is that somebody eventually reads the picture and the code in the same afternoon. The
/// C2 diagram in the same file spent an entire feature with no Support box in it, which is exactly
/// the failure this suite exists to stop repeating.
/// </para>
/// <para>
/// Two properties are checked. Every <c>*IntegrationEvent</c> declared in a module's
/// <c>IntegrationEvents</c> project is <b>named somewhere in the section</b> — including the six
/// that nothing consumes, which is where a topology diagram is most tempted to flatter itself. And
/// the diagram's edges are <b>exactly</b> the consumer registrations: every publisher→consumer pair
/// in <c>ConfigureConsumers</c> is drawn, and every pair drawn is real.
/// </para>
/// </summary>
public class IntegrationEventTopologyTests
{
    private const string SectionHeading = "### C3 — Event Topology";

    /// <summary>
    /// Mermaid node id → module name. The ids are short because they are typed on every edge; this
    /// is the one place that abbreviation is resolved.
    /// </summary>
    private static readonly Dictionary<string, string> DiagramNodes = new(StringComparer.Ordinal)
    {
        ["users"] = "Users",
        ["rest"] = "Restaurants",
        ["orders"] = "Orders",
        ["deliv"] = "Delivery",
        ["notif"] = "Notifications",
        ["rt"] = "RealTime",
        ["sup"] = "Support",
        ["pay"] = "Payments"
    };

    /// <summary>A published event and the module whose <c>IntegrationEvents</c> project declares it.</summary>
    private sealed record PublishedEvent(string Module, string Event);

    /// <summary>One arrow: <paramref name="Publisher"/> publishes <paramref name="Event"/>, <paramref name="Consumer"/> handles it.</summary>
    private sealed record Subscription(string Publisher, string Event, string Consumer);

    private static readonly Lazy<List<PublishedEvent>> Published = new(FindPublishedEvents);
    private static readonly Lazy<List<Subscription>> Subscriptions = new(FindSubscriptions);
    private static readonly Lazy<string> Section = new(ReadTopologySection);

    /// <summary>
    /// The vacuity guard the rest of the class rests on. Every assertion below is a set comparison,
    /// and a parser that silently found nothing would make all of them pass over empty collections —
    /// the failure mode <c>ObservabilityAssetTests</c> and <c>OpenApiDocumentTests</c> both had to
    /// grow a first test for.
    /// </summary>
    [Fact]
    public void TheParsers_Should_FindTheTopology()
    {
        Published.Value.Should().HaveCountGreaterThan(20, "every module publishes a handful of events");
        Subscriptions.Value.Should().HaveCountGreaterThan(20, "most of them are consumed somewhere");
        Section.Value.Should().Contain("```mermaid", "the section is supposed to contain the diagrams");

        Published.Value.Select(e => e.Module).Distinct().Should().HaveCountGreaterThan(3);
        Subscriptions.Value.Select(s => s.Consumer).Distinct().Should().HaveCountGreaterThan(3);
    }

    /// <summary>
    /// Every event the platform can publish is named in the section — the consumed ones on an edge,
    /// the unconsumed ones in the prose that lists them. An event added without touching the README
    /// fails here.
    /// </summary>
    [Fact]
    public void EveryPublishedEvent_Should_BeNamedInTheSection()
    {
        string section = Section.Value;

        string[] missing = Published.Value
            .Select(e => e.Event)
            .Distinct(StringComparer.Ordinal)
            .Where(name => !section.Contains(ShortName(name), StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        missing.Should().BeEmpty(
            "the C3 section must name every integration event, including the ones nothing consumes; " +
            "add {0} to the diagram or to the unconsumed-events list",
            string.Join(", ", missing));
    }

    /// <summary>
    /// The edges are the consumer registrations, both directions of the comparison. A new
    /// <c>AddConsumer</c> that nobody drew fails here, and so does an arrow drawn for a subscription
    /// that was removed.
    /// </summary>
    [Fact]
    public void TheDiagramEdges_Should_MatchTheConsumerRegistrations()
    {
        HashSet<Subscription> drawn = ParseDiagramEdges(Section.Value);
        HashSet<Subscription> registered = [.. Subscriptions.Value];

        string[] undrawn = registered.Except(drawn).Select(Describe).Order(StringComparer.Ordinal).ToArray();
        string[] imaginary = drawn.Except(registered).Select(Describe).Order(StringComparer.Ordinal).ToArray();

        undrawn.Should().BeEmpty(
            "these subscriptions exist in a module's ConfigureConsumers but no arrow in the README says so");
        imaginary.Should().BeEmpty(
            "these arrows are drawn in the README but no module consumes the event");
    }

    private static string Describe(Subscription subscription) =>
        $"{subscription.Publisher} -> {subscription.Consumer} : {ShortName(subscription.Event)}";

    private static string ShortName(string eventType) =>
        eventType.EndsWith("IntegrationEvent", StringComparison.Ordinal)
            ? eventType[..^"IntegrationEvent".Length]
            : eventType;

    // ---- the code side -------------------------------------------------------------------------

    private static string ModulesRoot => RepositoryPaths.Backend("src", "Modules");

    /// <summary>
    /// Every <c>sealed class X : IntegrationEvent</c> under a module's <c>IntegrationEvents</c>
    /// project. That project is the only place an event contract may live — Hard Rule #4 — so the
    /// directory layout is a reliable index of who owns what.
    /// </summary>
    private static List<PublishedEvent> FindPublishedEvents()
    {
        var events = new List<PublishedEvent>();
        var declaration = new Regex(@"class\s+(\w+IntegrationEvent)\b", RegexOptions.Compiled);

        foreach (string moduleDirectory in Directory.GetDirectories(ModulesRoot))
        {
            string module = Path.GetFileName(moduleDirectory);
            string contracts = Path.Combine(
                moduleDirectory,
                $"FoodDeliveryService.Modules.{module}.IntegrationEvents");

            if (!Directory.Exists(contracts))
            {
                continue;
            }

            foreach (string file in Directory.GetFiles(contracts, "*.cs", SearchOption.AllDirectories))
            {
                if (IsGenerated(file))
                {
                    continue;
                }

                foreach (Match match in declaration.Matches(File.ReadAllText(file)))
                {
                    events.Add(new PublishedEvent(module, match.Groups[1].Value));
                }
            }
        }

        return events;
    }

    /// <summary>
    /// Every integration event a module registers a consumer for, resolved back to the module that
    /// publishes it. Two registration shapes are in use and both are read: the generic
    /// <c>IntegrationEventConsumer&lt;T&gt;</c> that most modules use, and RealTime's hand-written
    /// consumer classes, which name their event on a base class instead.
    /// </summary>
    private static List<Subscription> FindSubscriptions()
    {
        var owner = Published.Value
            .GroupBy(e => e.Event, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Module, StringComparer.Ordinal);

        var generic = new Regex(@"AddConsumer<IntegrationEventConsumer<(\w+IntegrationEvent)>>", RegexOptions.Compiled);
        var named = new Regex(@"AddConsumer<(\w+)>", RegexOptions.Compiled);

        var subscriptions = new List<Subscription>();

        foreach (string moduleDirectory in Directory.GetDirectories(ModulesRoot))
        {
            string module = Path.GetFileName(moduleDirectory);
            string infrastructure = Path.Combine(
                moduleDirectory,
                $"FoodDeliveryService.Modules.{module}.Infrastructure");

            string registrationFile = Path.Combine(infrastructure, $"{module}Module.cs");
            if (!File.Exists(registrationFile))
            {
                continue;
            }

            string registrations = File.ReadAllText(registrationFile);
            Dictionary<string, string> consumerClasses = FindConsumerClassEvents(infrastructure);

            var consumed = new HashSet<string>(StringComparer.Ordinal);

            foreach (Match match in generic.Matches(registrations))
            {
                consumed.Add(match.Groups[1].Value);
            }

            foreach (Match match in named.Matches(registrations))
            {
                // Request/response consumers (GetUserPermissionsRequestConsumer and friends) name no
                // integration event and are deliberately absent from a topology of published events.
                if (consumerClasses.TryGetValue(match.Groups[1].Value, out string? eventType))
                {
                    consumed.Add(eventType);
                }
            }

            foreach (string eventType in consumed)
            {
                owner.Should().ContainKey(
                    eventType,
                    "{0} consumes {1}, which no IntegrationEvents project declares",
                    module,
                    eventType);

                subscriptions.Add(new Subscription(owner[eventType], eventType, module));
            }
        }

        return subscriptions;
    }

    /// <summary>
    /// Consumer class name → the integration event it handles, for consumers that are written out
    /// rather than closed over <c>IntegrationEventConsumer&lt;T&gt;</c> at the registration site.
    /// The event is read from the base list of the class declaration, so a primary constructor
    /// spanning several lines is matched the same as a one-line declaration.
    /// </summary>
    private static Dictionary<string, string> FindConsumerClassEvents(string infrastructureDirectory)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!Directory.Exists(infrastructureDirectory))
        {
            return map;
        }

        var declaration = new Regex(
            @"class\s+(\w*Consumer)\b[^{;]*?:\s*[^{;]*?<(\w+IntegrationEvent)>",
            RegexOptions.Compiled | RegexOptions.Singleline);

        foreach (string file in Directory.GetFiles(infrastructureDirectory, "*.cs", SearchOption.AllDirectories))
        {
            if (IsGenerated(file))
            {
                continue;
            }

            foreach (Match match in declaration.Matches(File.ReadAllText(file)))
            {
                map[match.Groups[1].Value] = match.Groups[2].Value;
            }
        }

        return map;
    }

    private static bool IsGenerated(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    // ---- the README side -----------------------------------------------------------------------

    /// <summary>
    /// The C3 section of the root README, from its heading to the next one at the same level or
    /// above. Everything asserted here is scoped to that slice, so an event name mentioned in an
    /// unrelated paragraph elsewhere in the file does not count as documenting the topology.
    /// </summary>
    private static string ReadTopologySection()
    {
        string path = RepositoryPaths.Backend("..", "README.md");
        string readme = File.ReadAllText(path);

        int start = readme.IndexOf(SectionHeading, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the README must contain a '{0}' section", SectionHeading);

        int next = readme.IndexOf("\n## ", start, StringComparison.Ordinal);
        int end = next < 0 ? readme.Length : next;

        return readme[start..end];
    }

    /// <summary>
    /// Reads the Mermaid arrows out of the section. An edge label carries one or more events
    /// separated by <c>·</c> or a line break, each written without its <c>IntegrationEvent</c>
    /// suffix — the suffix is on all 25 of them and repeating it would double the label width for no
    /// information.
    /// </summary>
    private static HashSet<Subscription> ParseDiagramEdges(string section)
    {
        var edge = new Regex(@"^\s*(\w+)\s*-->\|""(.+?)""\|\s*(\w+)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
        var edges = new HashSet<Subscription>();

        foreach (Match match in edge.Matches(section))
        {
            string source = match.Groups[1].Value;
            string target = match.Groups[3].Value;

            // The mechanism diagram above the topology one uses unlabelled arrows between nodes that
            // are not services; only the service nodes participate here.
            if (!DiagramNodes.TryGetValue(source, out string? publisher) ||
                !DiagramNodes.TryGetValue(target, out string? consumer))
            {
                continue;
            }

            foreach (string name in match.Groups[2].Value
                         .Replace("<br/>", "·", StringComparison.Ordinal)
                         .Split('·', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                edges.Add(new Subscription(publisher, $"{name}IntegrationEvent", consumer));
            }
        }

        return edges;
    }
}
