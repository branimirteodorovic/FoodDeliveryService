using System.Diagnostics.Metrics;
using AwesomeAssertions;
using FoodDeliveryService.Modules.Payments.Application.Diagnostics;
using FoodDeliveryService.Modules.Payments.Application.Payments.ReleasePayment;
using FoodDeliveryService.Modules.Payments.Domain.Payments;

namespace FoodDeliveryService.Modules.Payments.UnitTests;

/// <summary>
/// The Payments instruments — Feature 3.8 Milestone I. These assert the two things about telemetry
/// that fail silently in production and nowhere else.
/// <para>
/// <b>The names.</b> <c>ObservabilityAssetTests</c> cross-checks every Grafana panel and Prometheus
/// rule against a hand-written list of Prometheus names, and it cannot touch a module's instruments
/// — <c>Common.UnitTests</c> references no module. This is the other half of that check: rename
/// <c>payments.gateway.duration</c> here and the dashboard's PromQL is wrong, but only this file
/// says so.
/// </para>
/// <para>
/// <b>The tag values.</b> Every tag on these instruments is a constant from a bounded set, and the
/// whole reason those sets exist is that a Stripe message or an order id on a tag is a cardinality
/// explosion that takes Prometheus down rather than the service. A test that the reason reaching
/// <c>payments.failed</c> is one of six is cheap; discovering otherwise from a Prometheus that has
/// stopped is not.
/// </para>
/// </summary>
public class PaymentsDiagnosticsTests
{
    /// <summary>
    /// The instrument names exactly as <c>ObservabilityAssetTests.KnownMetrics</c> spells them, minus
    /// the Prometheus translation: dots for underscores, no <c>_total</c> on the counters and no
    /// <c>_seconds_bucket</c> on the histograms. Both halves have to be edited together, which is the
    /// point of writing them out twice.
    /// </summary>
    [Fact]
    public void EveryRecordMethod_Should_PublishTheInstrumentTheDashboardsQuery()
    {
        // Arrange
        using var recorder = new InstrumentRecorder(PaymentsDiagnostics.Name);

        // Act — one call per instrument, which is also what runs the static initialisers.
        PaymentsDiagnostics.RecordAuthorized();
        PaymentsDiagnostics.RecordCaptured();
        PaymentsDiagnostics.RecordFailed(PaymentFailureReason.CardDeclined);
        PaymentsDiagnostics.RecordReleased(PaymentReleaseTrigger.Rejected);
        PaymentsDiagnostics.RecordRefundSettled();
        PaymentsDiagnostics.RecordGatewayCall("authorize", "succeeded", 0.25);
        PaymentsDiagnostics.RecordWebhookLag("payment_intent.succeeded", 1.5);

        // Assert
        recorder.Instruments.Should().BeEquivalentTo(
            "payments.authorized",
            "payments.captured",
            "payments.failed",
            "payments.released",
            "refunds.settled",
            "payments.gateway.duration",
            "payments.webhook.lag");
    }

    /// <summary>
    /// The unit is not decoration: the Prometheus exporter reads it, and <c>s</c> is what turns
    /// <c>payments.gateway.duration</c> into <c>payments_gateway_duration_seconds_bucket</c> — the
    /// name the alert rule fires on. A histogram that shipped without it would export a family the
    /// rule never matches, which is an alert that is permanently silent and looks like health.
    /// </summary>
    [Fact]
    public void BothHistograms_Should_CarryTheSecondsUnit()
    {
        using var recorder = new InstrumentRecorder(PaymentsDiagnostics.Name);

        PaymentsDiagnostics.RecordGatewayCall("capture", "succeeded", 0.1);
        PaymentsDiagnostics.RecordWebhookLag("payment_intent.succeeded", 0.1);

        recorder.Units.Should().Contain(("payments.gateway.duration", "s"));
        recorder.Units.Should().Contain(("payments.webhook.lag", "s"));
    }

    [Fact]
    public void EveryFailureReason_Should_ReachTheTagUntranslated()
    {
        // Arrange
        using var recorder = new InstrumentRecorder(PaymentsDiagnostics.Name);

        // Act
        foreach (string reason in PaymentFailureReason.All)
        {
            PaymentsDiagnostics.RecordFailed(reason);
        }

        // Assert — six values, and the same six the integration event and the customer's email
        // carry. A translation step here would be a second vocabulary for one fact.
        recorder.TagValues("payments.failed", "reason")
            .Should().BeEquivalentTo(PaymentFailureReason.All);
    }

    [Fact]
    public void TheReleaseTrigger_Should_BeOneOfTwoBoundedValues()
    {
        using var recorder = new InstrumentRecorder(PaymentsDiagnostics.Name);

        PaymentsDiagnostics.RecordReleased(PaymentReleaseTrigger.Rejected);
        PaymentsDiagnostics.RecordReleased(PaymentReleaseTrigger.Cancelled);

        recorder.TagValues("payments.released", "trigger")
            .Should().BeEquivalentTo("rejected", "cancelled");
    }

    /// <summary>
    /// Listens to one meter and remembers what was recorded. The meter is process-wide (see
    /// <c>AppDiagnostics.Meter</c>), so the filter is by meter name and the assertions above are all
    /// written as "contains", never as "these and nothing else", for anything but the instrument set
    /// itself.
    /// </summary>
    private sealed class InstrumentRecorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Instrument, string? Unit, KeyValuePair<string, object?>[] Tags)> _measurements = [];
        private readonly Lock _gate = new();

        public InstrumentRecorder(string meterName)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == meterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>(
                (instrument, _, tags, _) => Record(instrument, tags));
            _listener.SetMeasurementEventCallback<double>(
                (instrument, _, tags, _) => Record(instrument, tags));
            _listener.Start();
        }

        public IReadOnlyCollection<string> Instruments
        {
            get
            {
                lock (_gate)
                {
                    return [.. _measurements.Select(measurement => measurement.Instrument).Distinct(StringComparer.Ordinal)];
                }
            }
        }

        public IReadOnlyCollection<(string Instrument, string? Unit)> Units
        {
            get
            {
                lock (_gate)
                {
                    return [.. _measurements.Select(measurement => (measurement.Instrument, measurement.Unit)).Distinct()];
                }
            }
        }

        public IReadOnlyCollection<string> TagValues(string instrument, string tag)
        {
            lock (_gate)
            {
                return
                [
                    .. _measurements
                        .Where(measurement => measurement.Instrument == instrument)
                        .SelectMany(measurement => measurement.Tags)
                        .Where(pair => pair.Key == tag)
                        .Select(pair => pair.Value?.ToString() ?? string.Empty)
                        .Distinct(StringComparer.Ordinal)
                ];
            }
        }

        public void Dispose() => _listener.Dispose();

        private void Record(Instrument instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            lock (_gate)
            {
                _measurements.Add((instrument.Name, instrument.Unit, tags.ToArray()));
            }
        }
    }
}
