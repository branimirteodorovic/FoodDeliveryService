using System.Diagnostics;
using AwesomeAssertions;
using FoodDeliveryService.Common.Presentation.Correlation;

namespace FoodDeliveryService.Common.UnitTests.Correlation;

/// <summary>
/// The context is what carries correlation past the end of a request, into rows that are dispatched
/// seconds later. Two behaviours matter: a value that was pushed is what everything downstream reads,
/// and — for work that has no request behind it at all — reading it never comes back empty when there
/// is an ambient trace to borrow from.
/// </summary>
public class CorrelationContextTests
{
    private const string ActivitySourceName = "FoodDeliveryService.Tests.CorrelationContext";

    [Fact]
    public void CorrelationId_Should_ReturnThePushedValue()
    {
        // Arrange
        var correlationContext = new CorrelationContext();

        // Act
        using IDisposable scope = correlationContext.Push("gateway-1234", "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");

        // Assert
        correlationContext.CorrelationId.Should().Be("gateway-1234");
        correlationContext.TraceParent.Should().Be("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");
    }

    [Fact]
    public void CorrelationId_Should_FallBackToTheAmbientActivity_WhenNothingPushedOne()
    {
        // Arrange — the case that exists because work can ORIGINATE in a background job: nothing
        // populated the context, and a null column would leave that flow uncorrelatable.
        using var source = new ActivitySource(ActivitySourceName);
        using ActivityListener listener = CreateListener();

        ActivitySource.AddActivityListener(listener);

        using Activity? activity = source.StartActivity("job tick");

        activity.Should().NotBeNull();

        var correlationContext = new CorrelationContext();

        // Assert
        correlationContext.CorrelationId.Should().Be(activity.TraceId.ToString());
        correlationContext.TraceParent.Should().Be(activity.Id);
    }

    [Fact]
    public void CorrelationId_Should_BeNull_WhenThereIsNeitherAPushedValueNorATrace()
    {
        // Arrange
        Activity.Current = null;

        var correlationContext = new CorrelationContext();

        // Assert — the columns are nullable precisely so this case writes nothing rather than an
        // invented id that ties two unrelated flows together.
        correlationContext.CorrelationId.Should().BeNull();
        correlationContext.TraceParent.Should().BeNull();
    }

    [Fact]
    public void Push_Should_RestoreThePreviousValue_OnDispose()
    {
        // Arrange — a dispatch job pushes once per message, in a loop. If a scope did not unwind,
        // message two would be logged under message one's correlation id.
        Activity.Current = null;

        var correlationContext = new CorrelationContext();

        using (correlationContext.Push("first", traceParent: null))
        {
            using (correlationContext.Push("second", traceParent: null))
            {
                correlationContext.CorrelationId.Should().Be("second");
            }

            // Assert
            correlationContext.CorrelationId.Should().Be("first");
        }

        correlationContext.CorrelationId.Should().BeNull();
    }

    [Fact]
    public async Task Push_Should_SurviveAnAwait()
    {
        // Arrange — everything that reads this does so after at least one await (the interceptor
        // inside SaveChangesAsync, the publish filter inside a handler).
        Activity.Current = null;

        var correlationContext = new CorrelationContext();

        // Act
        using IDisposable scope = correlationContext.Push("gateway-1234", traceParent: null);

        await Task.Yield();

        // Assert
        correlationContext.CorrelationId.Should().Be("gateway-1234");
    }

    [Fact]
    public void Push_Should_NotLeakBetweenInstances()
    {
        // Arrange — the integration tests run two hosts in one process; each host's context is its
        // own, so one host's request can never stamp the other host's outbox rows.
        Activity.Current = null;

        var one = new CorrelationContext();
        var other = new CorrelationContext();

        // Act
        using IDisposable scope = one.Push("orders-request", traceParent: null);

        // Assert
        other.CorrelationId.Should().BeNull();
    }

    private static ActivityListener CreateListener() =>
        new()
        {
            ShouldListenTo = source => source.Name == ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
}
