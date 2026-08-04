using System.Data.Common;
using System.Diagnostics;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Infrastructure.Correlation;
using FoodDeliveryService.Common.Infrastructure.Serialization;
using FoodDeliveryService.Common.Presentation.Correlation;
using FoodDeliveryService.Modules.Orders.Domain.Orders;
using FoodDeliveryService.Modules.Orders.IntegrationTests.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Serilog.Events;

namespace FoodDeliveryService.Modules.Orders.IntegrationTests.Telemetry;

/// <summary>
/// The two legs of a cross-service flow that Milestone D did not reach — the ones on the far side of
/// a database table. <c>CorrelationTests</c> proves the request leg and the broker hop; this proves
/// that the id survives <c>outbox_messages</c> and <c>inbox_messages</c>, which is where a message
/// waits with no request and no ambient activity behind it.
/// <para>
/// Every assertion here is on the real hosts: a real Postgres, a real RabbitMQ, the real Quartz jobs
/// on a one-second interval, and the in-memory Serilog sink and span exporters Milestone D added.
/// </para>
/// </summary>
public class OutboxInboxCorrelationTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task TheOutboxRow_Should_CarryTheRequestsCorrelationId()
    {
        // Arrange
        string inbound = $"outbox-row-{Guid.NewGuid():N}";

        HttpClient client = await GetAuthenticatedHttpClientAsync();

        client.DefaultRequestHeaders.Remove(CorrelationHeaders.CorrelationId);
        client.DefaultRequestHeaders.Add(CorrelationHeaders.CorrelationId, inbound);

        // Act
        PlacedOrder placedOrder = await PlaceOrderAsync(client);

        // Assert — a column, not a field inside the serialized content, precisely so this query is
        // possible: "which outbox rows belong to this correlation id?" is a support question of its
        // own, and re-shaping the event contract to answer it would touch every consumer.
        OutboxRow row = await GetOutboxRowAsync(inbound);

        row.Type.Should().Be(nameof(OrderPlacedDomainEvent));
        row.TraceParent.Should().MatchRegex("^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$");
        placedOrder.OrderId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task TheOutboxDispatch_Should_LogUnderTheRequestsCorrelationIdAndOrderId()
    {
        // Arrange
        string inbound = $"outbox-log-{Guid.NewGuid():N}";

        HttpClient client = await GetAuthenticatedHttpClientAsync();

        client.DefaultRequestHeaders.Remove(CorrelationHeaders.CorrelationId);
        client.DefaultRequestHeaders.Add(CorrelationHeaders.CorrelationId, inbound);

        // Act
        PlacedOrder placedOrder = await PlaceOrderAsync(client);

        // Assert — the dispatch runs seconds later, on a Quartz thread, with no HttpContext behind
        // it. `POST orders` carries no route id either, so a line holding BOTH this correlation id
        // and this OrderId cannot have come from the request: it is the asynchronous leg.
        Result<LogEvent> dispatched = await Poller.WaitAsync(
            DispatchTimeout,
            () => Task.FromResult(FindLogEvent(inbound, placedOrder.OrderId)));

        dispatched.IsSuccess.Should().BeTrue(
            "the outbox dispatch must log under the correlation id of the request that raised the event");

        Property(dispatched.Value, "TraceId").Should().MatchRegex("^[0-9a-f]{32}$");
        Property(dispatched.Value, "SpanId").Should().MatchRegex("^[0-9a-f]{16}$");
    }

    [Fact]
    public async Task TheDispatchSpan_Should_ReferenceTheTraceThatCausedTheMessage()
    {
        // Arrange
        string inbound = $"outbox-span-{Guid.NewGuid():N}";

        HttpClient client = await GetAuthenticatedHttpClientAsync();

        client.DefaultRequestHeaders.Remove(CorrelationHeaders.CorrelationId);
        client.DefaultRequestHeaders.Add(CorrelationHeaders.CorrelationId, inbound);

        await PlaceOrderAsync(client);

        OutboxRow row = await GetOutboxRowAsync(inbound);

        ActivityContext.TryParse(row.TraceParent, traceState: null, out ActivityContext origin)
            .Should().BeTrue("the row must store a parseable W3C traceparent");

        // Act
        Result<Activity> dispatch = await Poller.WaitAsync(
            DispatchTimeout,
            () => Task.FromResult(FindDispatchSpanLinkedTo(origin.TraceId)));

        // Assert — a LINK, not a parent: the dispatch is its own trace (one job execution drains a
        // batch belonging to N requests), and the link is what lets Jaeger navigate from it back to
        // the request that produced the message.
        dispatch.IsSuccess.Should().BeTrue("the dispatch span must reference the originating trace");

        dispatch.Value.TraceId.Should().NotBe(origin.TraceId);
        dispatch.Value.ParentSpanId.Should().Be(default(ActivitySpanId));
    }

    [Fact]
    public async Task TheInboxDispatch_Should_LogUnderTheProducingRequestsCorrelationId()
    {
        // Arrange — the far half of a cross-service flow. Registration happens in the Users host and
        // Orders learns of it only through UserRegisteredIntegrationEvent, so the id has to survive
        // Users' outbox, a MassTransit header, Orders' inbox row and Orders' inbox job.
        string inbound = $"inbox-{Guid.NewGuid():N}";

        // Act
        await RegisterCustomerAsync(inbound);

        // Assert
        Result<LogEvent> dispatched = await Poller.WaitAsync(
            DispatchTimeout,
            () => Task.FromResult(FindLogEvent(inbound, businessId: null)));

        dispatched.IsSuccess.Should().BeTrue(
            "the consuming service's inbox dispatch must log under the id of the request that caused it, " +
            "in the other service");
    }

    [Fact]
    public async Task APreMigrationOutboxRow_Should_StillDispatch()
    {
        // Arrange — a row written before the correlation columns existed: both are NULL. Nullable is
        // what makes the migration trivial, and this is the case that must never throw inside the
        // job loop and take the whole batch down with it.
        var domainEvent = new OrderPlacedDomainEvent(
            Guid.NewGuid(),
            Factory.TestUserId,
            Guid.NewGuid(),
            25.00m,
            DateTime.UtcNow);

        await InsertUncorrelatedOutboxRowAsync(domainEvent);

        // Act
        Result<bool> processed = await Poller.WaitAsync(DispatchTimeout, () => WasProcessedCleanlyAsync(domainEvent.Id));

        // Assert
        processed.IsSuccess.Should().BeTrue("a row with no correlation columns must dispatch exactly as it always did");
    }

    private async Task RegisterCustomerAsync(string correlationId)
    {
        HttpClient usersClient = Factory.UsersApi.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "users/register")
        {
            Content = JsonContent.Create(new
            {
                Email = $"orders-correlation+{Guid.NewGuid():N}@fooddeliveryservice.com",
                Password = "Orders-Correlation-P@ssw0rd1",
                FirstName = "Corr",
                LastName = "Elation"
            })
        };

        request.Headers.Add(CorrelationHeaders.CorrelationId, correlationId);

        HttpResponseMessage response = await usersClient.SendAsync(request, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// A log event carrying this correlation id and, when one is given, that business id — the two
    /// properties together are what "search for all logs related to one order" resolves to.
    /// </summary>
    private Result<LogEvent> FindLogEvent(string correlationId, Guid? businessId)
    {
        foreach (LogEvent logEvent in Factory.CollectLogEvents())
        {
            if (Property(logEvent, "CorrelationId") != correlationId)
            {
                continue;
            }

            if (businessId is null || Property(logEvent, "OrderId") == businessId.Value.ToString())
            {
                return Result.Success(logEvent);
            }
        }

        return Result.Failure<LogEvent>(Error.NullValue);
    }

    private Result<Activity> FindDispatchSpanLinkedTo(ActivityTraceId originTraceId)
    {
        foreach (Activity activity in Factory.CollectActivities())
        {
            if (activity.Source.Name != MessagingDiagnostics.Name)
            {
                continue;
            }

            if (activity.Links.Any(link => link.Context.TraceId == originTraceId))
            {
                return Result.Success(activity);
            }
        }

        return Result.Failure<Activity>(Error.NullValue);
    }

    private async Task<OutboxRow> GetOutboxRowAsync(string correlationId)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

        await using DbConnection connection = await connectionFactory.OpenConnectionAsync();

        const string sql =
            """
            SELECT type AS Type, correlation_id AS CorrelationId, trace_parent AS TraceParent
            FROM outbox_messages
            WHERE correlation_id = @CorrelationId
            """;

        IEnumerable<OutboxRow> rows = await connection.QueryAsync<OutboxRow>(sql, new { CorrelationId = correlationId });

        return rows.Should().ContainSingle().Subject;
    }

    private async Task InsertUncorrelatedOutboxRowAsync(OrderPlacedDomainEvent domainEvent)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

        await using DbConnection connection = await connectionFactory.OpenConnectionAsync();

        const string sql =
            """
            INSERT INTO outbox_messages(id, type, content, occurred_on_utc)
            VALUES (@Id, @Type, @Content::jsonb, @OccurredOnUtc)
            """;

        await connection.ExecuteAsync(
            sql,
            new
            {
                domainEvent.Id,
                Type = nameof(OrderPlacedDomainEvent),
                Content = JsonConvert.SerializeObject(domainEvent, SerializerSettings.Instance),
                domainEvent.OccurredOnUtc
            });
    }

    private async Task<Result<bool>> WasProcessedCleanlyAsync(Guid outboxMessageId)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

        await using DbConnection connection = await connectionFactory.OpenConnectionAsync();

        const string sql =
            """
            SELECT COUNT(*)
            FROM outbox_messages
            WHERE id = @Id AND processed_on_utc IS NOT NULL AND error IS NULL
            """;

        long count = await connection.ExecuteScalarAsync<long>(sql, new { Id = outboxMessageId });

        return count > 0 ? Result.Success(true) : Result.Failure<bool>(Error.NullValue);
    }

    private static string? Property(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out LogEventPropertyValue? value) && value is ScalarValue scalar
            ? scalar.Value?.ToString()
            : null;

    private sealed record OutboxRow(string Type, string? CorrelationId, string? TraceParent);
}
