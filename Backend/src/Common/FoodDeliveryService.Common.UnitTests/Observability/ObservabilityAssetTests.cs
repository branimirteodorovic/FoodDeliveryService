using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using FoodDeliveryService.Common.Application.Diagnostics;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Infrastructure.Caching;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FoodDeliveryService.Common.UnitTests.Observability;

/// <summary>
/// Milestone E is infrastructure and configuration, so there is nothing to unit-test in the usual
/// sense — but its failure mode is specific and silent: a dashboard whose PromQL names a metric no
/// service emits renders an empty panel, and an alert on the same name never fires. Neither says
/// anything; both look like "nothing is happening".
/// <para>
/// These tests are the schema check the plan asks for. They parse every asset under
/// <c>Backend/docker</c> for real, and — the part that actually earns its keep — cross-check every
/// metric name the dashboards and alerts reference against the set the code emits, translated to
/// Prometheus naming. Renaming an instrument now fails a test instead of blanking a panel.
/// </para>
/// </summary>
public class ObservabilityAssetTests
{
    /// <summary>
    /// Every metric name a dashboard panel or an alert rule is allowed to reference, in the form the
    /// OpenTelemetry Collector's Prometheus exporter publishes it: dots become underscores, a
    /// monotonic counter gains <c>_total</c>, a histogram with unit <c>s</c> becomes
    /// <c>_seconds_bucket</c>/<c>_sum</c>/<c>_count</c>, and a unit in braces (<c>{order}</c>) is an
    /// annotation that is dropped rather than a suffix.
    /// <para>
    /// The <c>app.*</c> and <c>cache.*</c> entries are pinned to the real instruments by
    /// <see cref="KnownMetricNames_Should_MatchTheInstrumentsCommonCreates"/>. The Orders and
    /// Delivery ones cannot be: <c>Common.UnitTests</c> references no module, by the same convention
    /// that keeps <c>{Module}.UnitTests</c> on its own Domain. They are listed from
    /// <c>OrdersDiagnostics</c> and <c>DeliveryAssignmentDiagnostics</c>, and the integration suites
    /// in those modules are what assert the instruments still exist.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> KnownMetrics =
    [
        // ASP.NET Core instrumentation, via AddHostTelemetry (Milestone A).
        "http_server_request_duration_seconds_bucket",
        "http_server_request_duration_seconds_count",
        "http_server_request_duration_seconds_sum",

        // ApplicationDiagnostics — the application-boundary RED signal (Milestone B).
        "app_requests_total",
        "app_request_failures_total",
        "app_request_duration_seconds_bucket",
        "app_request_duration_seconds_count",
        "app_request_duration_seconds_sum",

        // OrdersDiagnostics (Milestone B).
        "orders_placed_total",
        "orders_state_transition_total",

        // DeliveryAssignmentDiagnostics (Milestone B).
        "delivery_assignment_outcome_total",
        "delivery_assignment_duration_seconds_bucket",
        "delivery_assignment_duration_seconds_count",
        "delivery_assignment_duration_seconds_sum",

        // CacheDiagnostics (Caching 2.3 Milestone E), collected from Telemetry Milestone A.
        "cache_hits_total",
        "cache_misses_total",

        // The blackbox exporter probing Milestone C's endpoints — the only metric here that is not
        // emitted by the application itself.
        "probe_success",
        "probe_duration_seconds",

        // ── Pushed by the k6 load generator, not emitted by any service ──────────────────────────
        //
        // Read that again before assuming something is missing from the code: these arrive over
        // Prometheus' remote-write endpoint while a load test is running (Feature 3.5 Milestone E,
        // LOADTESTING_PHASE3_PLAN.md §6) and they exist only for the duration of a run. They occupy
        // the same exception `probe_success` above does — a metric this repository owns the
        // *producer* of, just not inside a .NET process.
        //
        // The names are k6's remote-write translation and it is mechanical: `k6_` prefix, then the
        // metric type decides the suffix. A counter gains `_total`, a rate gains `_rate`, a gauge
        // gains nothing, and a trend becomes one gauge per statistic in K6_PROMETHEUS_RW_TREND_STATS
        // (`p(95)` → `_p95`), which is set in docker-compose.yml. Custom metrics come from
        // `loadtest/lib/metrics.js` — `orders_placed` there is `k6_orders_placed_total` here.
        "k6_vus",
        "k6_http_reqs_total",
        "k6_http_req_duration_p95",
        "k6_checks_rate",
        "k6_dropped_iterations_total",
        "k6_orders_placed_total",
        "k6_order_placement_duration_p95"
    ];

    /// <summary>
    /// The prefix that marks a series as pushed by the load generator rather than emitted by a
    /// service. Used to keep those series on the dashboard that expects them to be absent most of
    /// the time — see <see cref="K6Series_Should_OnlyBeQueried_ByTheLoadDashboard"/>.
    /// </summary>
    private const string LoadGeneratorPrefix = "k6_";

    private const string LoadDashboardFile = "load.json";

    /// <summary>
    /// A metric reference in PromQL is always an identifier immediately followed by a label matcher
    /// or a range selector — <c>cache_hits_total{...}</c>, <c>orders_placed_total[5m]</c>. Function
    /// names are followed by <c>(</c> and label names by <c>=</c>, so neither is matched.
    /// </summary>
    private static readonly Regex MetricReference = new(
        @"(?<name>[a-zA-Z_][a-zA-Z0-9_]*)\s*(?=[{\[])",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private const string PrometheusDatasourceUid = "fooddelivery-prometheus";
    private const string CollectorEndpoint = "http://fooddeliveryservice.otel-collector:4317";

    public static TheoryData<string> DashboardFiles()
    {
        var data = new TheoryData<string>();

        foreach (string file in Directory.GetFiles(BackendPath("docker", "grafana", "dashboards"), "*.json"))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    public static TheoryData<string> HostSettingsFiles()
    {
        var data = new TheoryData<string>();

        // One level only, deliberately: a recursive search also finds the copies under bin/, which
        // are whatever the last build put there rather than what the repository says.
        foreach (string host in Directory.GetDirectories(BackendPath("src", "API")))
        {
            string file = Path.Combine(host, "appsettings.Development.json");

            if (File.Exists(file))
            {
                data.Add(Path.GetRelativePath(BackendPath(), file));
            }
        }

        return data;
    }

    [Fact]
    public void Dashboards_Should_BeProvisioned_ForEveryStoryTheMilestonePromises()
    {
        IEnumerable<string> uids = DashboardDocuments()
            .Select(dashboard => dashboard.RootElement.GetProperty("uid").GetString()!);

        // RED, business and cache hit-rate: the three Telemetry 2.4 §6 names by hand, plus the load
        // dashboard Feature 3.5 Milestone E adds — a k6 run read next to the platform's own metrics.
        uids.Should().BeEquivalentTo("fds-red", "fds-business", "fds-cache", "fds-load");
    }

    [Theory]
    [MemberData(nameof(DashboardFiles))]
    public void K6Series_Should_OnlyBeQueried_ByTheLoadDashboard(string fileName)
    {
        using JsonDocument dashboard = ReadJson(BackendPath("docker", "grafana", "dashboards", fileName));

        List<string> k6Series =
        [
            .. Expressions(dashboard)
                .SelectMany(MetricNamesIn)
                .Where(metric => metric.StartsWith(LoadGeneratorPrefix, StringComparison.Ordinal))
        ];

        if (fileName == LoadDashboardFile)
        {
            // The load dashboard's whole point is the client's view next to the server's, so it is
            // the one file that must have both.
            k6Series.Should().NotBeEmpty("{0} exists to show what the load generator measured", fileName);
            return;
        }

        // Everywhere else they are a trap. A k6 series has data only while a load test is streaming
        // into Prometheus, so a panel on an always-on dashboard would be empty on every ordinary day
        // and read as broken instrumentation.
        k6Series.Should().BeEmpty(
            "{0} is read when no load test is running, and k6 series do not exist then", fileName);
    }

    [Theory]
    [MemberData(nameof(DashboardFiles))]
    public void Dashboard_Should_BeValidJson_WithPanelsAndAStableUid(string fileName)
    {
        // Act
        using JsonDocument dashboard = ReadJson(BackendPath("docker", "grafana", "dashboards", fileName));

        // Assert — the uid and title are what the provisioning file keys on; a dashboard without a
        // fixed uid gets a new one on every import and every link to it rots.
        JsonElement root = dashboard.RootElement;
        root.GetProperty("uid").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("schemaVersion").GetInt32().Should().BeGreaterThan(0);
        root.GetProperty("panels").EnumerateArray().Should().NotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(DashboardFiles))]
    public void DashboardPanel_Should_QueryTheProvisionedPrometheusDatasource(string fileName)
    {
        using JsonDocument dashboard = ReadJson(BackendPath("docker", "grafana", "dashboards", fileName));

        foreach (JsonElement panel in QueryPanels(dashboard))
        {
            // A panel that names a datasource uid Grafana was never provisioned with renders an
            // error rather than a graph, and the uid is the one thing an author cannot see is wrong.
            panel.GetProperty("datasource").GetProperty("uid").GetString()
                .Should().Be(PrometheusDatasourceUid, "panel '{0}' in {1}", Title(panel), fileName);

            foreach (JsonElement target in panel.GetProperty("targets").EnumerateArray())
            {
                target.GetProperty("expr").GetString()
                    .Should().NotBeNullOrWhiteSpace("every target in '{0}' needs a query", Title(panel));
            }
        }
    }

    [Theory]
    [MemberData(nameof(DashboardFiles))]
    public void DashboardExpressions_Should_OnlyReferenceMetricsTheServicesEmit(string fileName)
    {
        using JsonDocument dashboard = ReadJson(BackendPath("docker", "grafana", "dashboards", fileName));

        List<string> referenced = [.. Expressions(dashboard).SelectMany(MetricNamesIn)];

        // Without this, an extraction that silently matched nothing would make the assertion below
        // pass on an empty set — the failure mode a test like this is most likely to have.
        referenced.Should().NotBeEmpty();

        referenced.Should().OnlyContain(
            metric => KnownMetrics.Contains(metric),
            "{0} must not query a metric no service emits — the panel would just be empty",
            fileName);
    }

    [Fact]
    public void AlertRules_Should_ParseAndCarryASeverityAndASummary()
    {
        // Act
        AlertRuleFile rules = Yaml.Deserialize<AlertRuleFile>(
            File.ReadAllText(BackendPath("docker", "prometheus", "rules", "alerts.yml")));

        // Assert
        rules.Groups.Should().NotBeEmpty();
        rules.Groups.Select(group => group.Name).Should().OnlyHaveUniqueItems();

        List<AlertRule> all = [.. rules.Groups.SelectMany(group => group.Rules)];

        all.Should().NotBeEmpty();
        all.Select(rule => rule.Alert).Should().OnlyHaveUniqueItems();

        foreach (AlertRule rule in all)
        {
            rule.Alert.Should().NotBeNullOrWhiteSpace();
            rule.Expr.Should().NotBeNullOrWhiteSpace();

            // `for` is what separates an alert from a blip. A rule without one fires on a single
            // scrape, which for a ratio over a 5-minute rate is almost always noise.
            rule.For.Should().NotBeNullOrWhiteSpace("{0} must state how long it has to hold", rule.Alert);
            rule.Labels.Should().ContainKey("severity");
            rule.Annotations.Should().ContainKey("summary");
            rule.Annotations.Should().ContainKey("description");
        }
    }

    [Fact]
    public void AlertExpressions_Should_OnlyReferenceMetricsTheServicesEmit()
    {
        AlertRuleFile rules = Yaml.Deserialize<AlertRuleFile>(
            File.ReadAllText(BackendPath("docker", "prometheus", "rules", "alerts.yml")));

        List<string> referenced =
        [
            .. rules.Groups.SelectMany(group => group.Rules).SelectMany(rule => MetricNamesIn(rule.Expr))
        ];

        // An alert on a misspelled metric is worse than no alert: it is permanently silent and looks
        // like health.
        referenced.Should().NotBeEmpty();
        referenced.Should().OnlyContain(metric => KnownMetrics.Contains(metric));
    }

    [Fact]
    public void ProvisionedDatasources_Should_CoverEveryUidTheDashboardsReference()
    {
        // Arrange
        DatasourceFile datasources = Yaml.Deserialize<DatasourceFile>(
            File.ReadAllText(BackendPath(
                "docker", "grafana", "provisioning", "datasources", "datasources.yml")));

        // Assert — the uids are fixed in the provisioning file precisely so the dashboards can name
        // them; letting Grafana generate them would make every dashboard machine-specific.
        datasources.Datasources.Select(datasource => datasource.Uid)
            .Should().Contain(PrometheusDatasourceUid);

        datasources.Datasources.Should().Contain(datasource => datasource.Type == "jaeger");
    }

    [Fact]
    public void DashboardProvider_Should_PointAtThePathDockerComposeMounts()
    {
        // Arrange
        DashboardProviderFile providers = Yaml.Deserialize<DashboardProviderFile>(
            File.ReadAllText(BackendPath(
                "docker", "grafana", "provisioning", "dashboards", "dashboards.yml")));

        string compose = File.ReadAllText(BackendPath("docker-compose.yml"));

        // Assert — two files, one path, and nothing at runtime that would say they disagreed: a
        // provider pointing at an unmounted directory provisions zero dashboards, silently.
        string path = providers.Providers.Should().ContainSingle().Subject.Options.Path;

        compose.Should().Contain($":{path}:ro");
    }

    [Theory]
    [MemberData(nameof(HostSettingsFiles))]
    public void Host_Should_ExportOtlpToTheCollector_NotStraightToJaeger(string relativePath)
    {
        // Arrange
        using JsonDocument settings = ReadJson(BackendPath(relativePath));

        if (!settings.RootElement.TryGetProperty("OTEL_EXPORTER_OTLP_ENDPOINT", out JsonElement endpoint))
        {
            Assert.Fail($"{relativePath} has no OTEL_EXPORTER_OTLP_ENDPOINT — every host emits telemetry.");
            return;
        }

        // Assert — Jaeger accepts traces and drops metrics on the floor, which is exactly how
        // Milestones A and B shipped instrumentation that nothing collected. The collector is the
        // one endpoint a host is allowed to know.
        endpoint.GetString().Should().Be(CollectorEndpoint);
    }

    [Fact]
    public void KnownMetricNames_Should_MatchTheInstrumentsCommonCreates()
    {
        // Arrange — touching the diagnostics classes is what runs their static initialisers and
        // publishes the instruments to the listener.
        using var recorder = new InstrumentRecorder(ApplicationDiagnostics.Name, CacheDiagnostics.MeterName);

        // Act
        ApplicationDiagnostics.RecordSuccess(nameof(KnownMetricNames_Should_MatchTheInstrumentsCommonCreates), 0.01);
        ApplicationDiagnostics.RecordFailure("Probe", ErrorType.Failure, 0.01);
        CacheDiagnostics.RecordHit("observability-tests:probe");
        CacheDiagnostics.RecordMiss("observability-tests:probe");

        // Assert — the dashboards query the Prometheus translation of these names, so a rename that
        // did not reach docker/grafana lands here rather than on an empty panel.
        recorder.Instruments.Should().BeEquivalentTo(
            "app.requests",
            "app.request.duration",
            "app.request.failures",
            "cache.hits",
            "cache.misses");

        recorder.Instruments.Select(ToPrometheusFamily).Should().BeSubsetOf(
            KnownMetrics.Select(metric => metric
                .Replace("_total", string.Empty, StringComparison.Ordinal)
                .Replace("_bucket", string.Empty, StringComparison.Ordinal)
                .Replace("_sum", string.Empty, StringComparison.Ordinal)
                .Replace("_count", string.Empty, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Rows and collapsed-row containers hold no query of their own, and a text panel is prose.
    /// Everything else is expected to have a datasource and at least one target.
    /// </summary>
    private static IEnumerable<JsonElement> QueryPanels(JsonDocument dashboard) =>
        dashboard.RootElement.GetProperty("panels").EnumerateArray()
            .Where(panel => panel.GetProperty("type").GetString() is not ("row" or "text"));

    /// <summary>Every PromQL expression a dashboard's panels query.</summary>
    private static IEnumerable<string> Expressions(JsonDocument dashboard) =>
        QueryPanels(dashboard)
            .SelectMany(panel => panel.GetProperty("targets").EnumerateArray())
            .Select(target => target.GetProperty("expr").GetString()!);

    private static string Title(JsonElement panel) =>
        panel.TryGetProperty("title", out JsonElement title) ? title.GetString() ?? "?" : "?";

    private static IEnumerable<string> MetricNamesIn(string expression) =>
        MetricReference.Matches(expression).Select(match => match.Groups["name"].Value);

    /// <summary>
    /// <c>app.request.duration</c> -&gt; <c>app_request_duration_seconds</c>: the family name, without
    /// the per-series suffix the exporter adds. Only the two second-valued histograms need the unit.
    /// </summary>
    private static string ToPrometheusFamily(string instrument)
    {
        string name = instrument.Replace('.', '_');

        return name.EndsWith("duration", StringComparison.Ordinal) ? $"{name}_seconds" : name;
    }

    private static IEnumerable<JsonDocument> DashboardDocuments() =>
        Directory.GetFiles(BackendPath("docker", "grafana", "dashboards"), "*.json").Select(ReadJson);

    private static JsonDocument ReadJson(string path) =>
        JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

    /// <summary>
    /// These assets live in the repository, not in the test output, so the tests walk up from the
    /// assembly location to the one directory that owns both <c>docker-compose.yml</c> and
    /// <c>docker/</c>.
    /// </summary>
    private static string BackendPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")) &&
                Directory.Exists(Path.Combine(directory.FullName, "docker")))
            {
                return Path.Combine([directory.FullName, .. segments]);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"No Backend root (docker-compose.yml + docker/) above {AppContext.BaseDirectory}.");
    }

    private sealed class InstrumentRecorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly HashSet<string> _instruments = [];
        private readonly Lock _gate = new();

        public InstrumentRecorder(params string[] meterNames)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (meterNames.Contains(instrument.Meter.Name))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => Record(instrument));
            _listener.SetMeasurementEventCallback<double>((instrument, _, _, _) => Record(instrument));
            _listener.Start();
        }

        public IReadOnlyCollection<string> Instruments
        {
            get
            {
                lock (_gate)
                {
                    return [.. _instruments];
                }
            }
        }

        public void Dispose() => _listener.Dispose();

        private void Record(Instrument instrument)
        {
            lock (_gate)
            {
                _instruments.Add(instrument.Name);
            }
        }
    }

    private sealed class AlertRuleFile
    {
        public List<AlertGroup> Groups { get; set; } = [];
    }

    private sealed class AlertGroup
    {
        public string Name { get; set; } = string.Empty;

        public List<AlertRule> Rules { get; set; } = [];
    }

    private sealed class AlertRule
    {
        public string Alert { get; set; } = string.Empty;

        public string Expr { get; set; } = string.Empty;

        public string For { get; set; } = string.Empty;

        public Dictionary<string, string> Labels { get; set; } = [];

        public Dictionary<string, string> Annotations { get; set; } = [];
    }

    private sealed class DatasourceFile
    {
        public List<Datasource> Datasources { get; set; } = [];
    }

    private sealed class Datasource
    {
        public string Uid { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;
    }

    private sealed class DashboardProviderFile
    {
        public List<DashboardProvider> Providers { get; set; } = [];
    }

    private sealed class DashboardProvider
    {
        public DashboardProviderOptions Options { get; set; } = new();
    }

    private sealed class DashboardProviderOptions
    {
        public string Path { get; set; } = string.Empty;
    }
}
