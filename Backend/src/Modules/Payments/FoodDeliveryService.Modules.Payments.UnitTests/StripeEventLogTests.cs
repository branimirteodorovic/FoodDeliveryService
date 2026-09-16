using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Domain.Webhooks;

namespace FoodDeliveryService.Modules.Payments.UnitTests;

/// <summary>
/// Feature 3.8 Milestone E, §7.4 — the aggregate behind the webhook dedupe table.
/// <para>
/// The unique index is what actually deduplicates, and a unit test cannot see an index. What it can
/// see is the part the index does not cover: that recording raises the event the outbox needs, that
/// marking is idempotent in both directions, and that a failure leaves the row reading as
/// outstanding — which matters because neither outbox job retries (§5.6), so this row is the only
/// record that the platform still owes this event some work.
/// </para>
/// </summary>
public class StripeEventLogTests
{
    private const string ProviderEventId = "evt_webhook_tests";
    private const string EventType = "setup_intent.succeeded";

    private static readonly DateTime ReceivedOn = new(2026, 9, 13, 10, 0, 0, DateTimeKind.Utc);

    private static Result<StripeEventLog> Record(
        string providerEventId = ProviderEventId,
        string eventType = EventType) =>
        StripeEventLog.Record(
            Guid.CreateVersion7(),
            providerEventId,
            eventType,
            objectId: "seti_webhook_tests",
            objectStatus: "succeeded",
            customerReference: "cus_webhook_tests",
            paymentMethodReference: "pm_webhook_tests",
            // Milestone F's two columns. Null on a setup_intent.succeeded, which is what this
            // fixture is: neither an order nor a refusal is anywhere near it.
            orderReference: null,
            failureReason: null,
            ReceivedOn);

    [Fact]
    public void Record_Should_CaptureTheProvidersFacts()
    {
        Result<StripeEventLog> result = Record();

        result.IsSuccess.Should().BeTrue();

        StripeEventLog log = result.Value;

        log.ProviderEventId.Should().Be(ProviderEventId);
        log.EventType.Should().Be(EventType);
        log.ObjectId.Should().Be("seti_webhook_tests");
        log.ObjectStatus.Should().Be("succeeded");
        log.CustomerReference.Should().Be("cus_webhook_tests");
        log.PaymentMethodReference.Should().Be("pm_webhook_tests");
        log.ReceivedOnUtc.Should().Be(ReceivedOn);
    }

    [Fact]
    public void Record_Should_RaiseTheEventTheOutboxDispatches()
    {
        // The whole of §7.6 rests on this: the request's remaining work is an insert and a 2xx, and
        // the actual work is driven by this domain event a second later. No event, no work, and
        // nothing anywhere says so.
        StripeEventLog log = Record().Value;

        StripeEventReceivedDomainEvent domainEvent =
            log.DomainEvents.OfType<StripeEventReceivedDomainEvent>().Single();

        domainEvent.EventLogId.Should().Be(log.Id);
        domainEvent.ProviderEventId.Should().Be(ProviderEventId);
        domainEvent.EventType.Should().Be(EventType);
        domainEvent.ReceivedOnUtc.Should().Be(ReceivedOn);
    }

    [Fact]
    public void Record_Should_BeUnprocessed()
    {
        StripeEventLog log = Record().Value;

        log.IsProcessed.Should().BeFalse();
        log.ProcessedOnUtc.Should().BeNull();
        log.Error.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Record_Should_RefuseAnEventWithNoProviderIdentifier(string providerEventId)
    {
        // The provider id is the dedupe key. A row without one cannot be deduplicated at all, so a
        // redelivery would be acted on a second time.
        Result<StripeEventLog> result = Record(providerEventId: providerEventId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StripeEventLogErrors.ProviderEventIdRequired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Record_Should_RefuseAnEventWithNoType(string eventType)
    {
        // The type is the dispatch key; without it the handler cannot decide what the event means.
        Result<StripeEventLog> result = Record(eventType: eventType);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StripeEventLogErrors.EventTypeRequired);
    }

    [Fact]
    public void MarkProcessed_Should_StampTheTime()
    {
        StripeEventLog log = Record().Value;
        DateTime processedOn = ReceivedOn.AddSeconds(2);

        log.MarkProcessed(processedOn);

        log.IsProcessed.Should().BeTrue();
        log.ProcessedOnUtc.Should().Be(processedOn);
    }

    [Fact]
    public void MarkProcessed_Should_KeepTheFirstTime_OnASecondDispatch()
    {
        // ProcessOutboxJob dispatches at least once. A second dispatch that overwrote the stamp
        // would make the log say the event was handled later than it was.
        StripeEventLog log = Record().Value;
        DateTime first = ReceivedOn.AddSeconds(2);

        log.MarkProcessed(first);
        log.MarkProcessed(first.AddHours(1));

        log.ProcessedOnUtc.Should().Be(first);
    }

    [Fact]
    public void MarkFailed_Should_RecordTheReason_AndLeaveTheRowOutstanding()
    {
        StripeEventLog log = Record().Value;

        log.MarkFailed("The provider customer is unknown to this service");

        log.Error.Should().Be("The provider customer is unknown to this service");

        // Not processed. Neither outbox job retries, so an unprocessed row carrying an error is the
        // only trace that this event still owes the platform some work.
        log.IsProcessed.Should().BeFalse();
        log.ProcessedOnUtc.Should().BeNull();
    }

    [Fact]
    public void MarkFailed_Should_NotReopenAnEventThatSucceeded()
    {
        StripeEventLog log = Record().Value;

        log.MarkProcessed(ReceivedOn.AddSeconds(2));
        log.MarkFailed("a late failure from a duplicate dispatch");

        log.IsProcessed.Should().BeTrue();
        log.Error.Should().BeNull();
    }

    [Fact]
    public void MarkProcessed_Should_ClearAnEarlierError()
    {
        // A fresh delivery of an event that failed the first time is the reconciling path (§7.4).
        // Leaving the old reason on a row that has since succeeded would send an operator after a
        // problem that no longer exists.
        StripeEventLog log = Record().Value;

        log.MarkFailed("a transient provider fault");
        log.MarkProcessed(ReceivedOn.AddMinutes(5));

        log.Error.Should().BeNull();
    }
}
