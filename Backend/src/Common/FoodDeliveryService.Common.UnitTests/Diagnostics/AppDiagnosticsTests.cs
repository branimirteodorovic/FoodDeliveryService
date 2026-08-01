using System.Diagnostics;
using System.Diagnostics.Metrics;
using AwesomeAssertions;
using FoodDeliveryService.Common.Application.Diagnostics;
using FoodDeliveryService.Common.Infrastructure.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace FoodDeliveryService.Common.UnitTests.Diagnostics;

/// <summary>
/// What is worth testing about the helper is not that it holds two objects — it is that
/// <c>AddModuleDiagnostics</c> registers BOTH of them, because an unregistered source or meter fails
/// silently: the code keeps recording and the measurements go nowhere. So every assertion here runs
/// through a real <see cref="TracerProvider"/>/<see cref="MeterProvider"/> built from a service
/// collection, not through a bare listener that would pass whether or not registration happened.
/// </summary>
public class AppDiagnosticsTests
{
    [Fact]
    public void AddModuleDiagnostics_Should_RegisterTheActivitySource()
    {
        // Arrange — a name unique per test: the sources and meters are process-wide.
        var diagnostics = new AppDiagnostics(UniqueName());

        var exportedActivities = new List<Activity>();

        using TelemetryHost host = BuildHost(
            diagnostics.Name,
            tracing => tracing.AddInMemoryExporter(exportedActivities),
            _ => { });

        // Act
        using (Activity? activity = diagnostics.ActivitySource.StartActivity("TestOperation"))
        {
            // Assert — a source with no registered listener returns null here, which is exactly the
            // failure mode a hand-written AddSource line used to be one typo away from.
            activity.Should().NotBeNull();
        }

        host.TracerProvider.ForceFlush();

        exportedActivities.Should().ContainSingle()
            .Which.OperationName.Should().Be("TestOperation");
    }

    [Fact]
    public void AddModuleDiagnostics_Should_RegisterTheMeter()
    {
        // Arrange
        var diagnostics = new AppDiagnostics(UniqueName());

        Counter<long> counter = diagnostics.Meter.CreateCounter<long>(
            "smoke.operations",
            unit: "{operation}",
            description: "Test counter.");

        var exportedMetrics = new List<Metric>();

        using TelemetryHost host = BuildHost(
            diagnostics.Name,
            _ => { },
            metrics => metrics.AddInMemoryExporter(exportedMetrics));

        // Act
        counter.Add(1, new KeyValuePair<string, object?>("outcome", "ok"));

        host.MeterProvider.ForceFlush();

        // Assert — the half that was missing before this milestone: DeliveryDiagnostics and
        // RealTimeDiagnostics each had a hand-written AddSource and no AddMeter at all, so a counter
        // added to either module would have been collected by nothing.
        Metric metric = exportedMetrics.Should().ContainSingle().Subject;

        metric.Name.Should().Be("smoke.operations");
        metric.Unit.Should().Be("{operation}");
        metric.MeterName.Should().Be(diagnostics.Name);
    }

    [Fact]
    public void AppDiagnostics_Should_NameTheSourceAndTheMeterIdentically()
    {
        // Arrange
        string name = UniqueName();

        // Act
        var diagnostics = new AppDiagnostics(name);

        // Assert — one name for both pillars is what lets a host wire them with a single
        // AddModuleDiagnostics(Name) call instead of two that can disagree.
        diagnostics.Name.Should().Be(name);
        diagnostics.ActivitySource.Name.Should().Be(name);
        diagnostics.Meter.Name.Should().Be(name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AppDiagnostics_Should_Reject_ABlankName(string name)
    {
        // Act
        Action act = () => _ = new AppDiagnostics(name);

        // Assert — an empty name would register nothing and silently swallow every measurement.
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Mirrors what a host does: <c>AddModuleDiagnostics(name)</c> alongside an
    /// <c>AddOpenTelemetry()</c> builder, then let DI build both providers. Resolving them is what
    /// starts the listeners, so it has to happen before the activity or measurement under test —
    /// outside a hosted app nothing else forces the build.
    /// </summary>
    private static TelemetryHost BuildHost(
        string name,
        Action<TracerProviderBuilder> configureTracing,
        Action<MeterProviderBuilder> configureMetrics)
    {
        var services = new ServiceCollection();

        services.AddModuleDiagnostics(name);

        services
            .AddOpenTelemetry()
            .WithTracing(configureTracing)
            .WithMetrics(configureMetrics);

        return new TelemetryHost(services.BuildServiceProvider());
    }

    private static string UniqueName() => $"FoodDeliveryService.Tests.{Guid.NewGuid():N}";

    private sealed class TelemetryHost(ServiceProvider provider) : IDisposable
    {
        public TracerProvider TracerProvider { get; } = provider.GetRequiredService<TracerProvider>();

        public MeterProvider MeterProvider { get; } = provider.GetRequiredService<MeterProvider>();

        public void Dispose() => provider.Dispose();
    }
}
