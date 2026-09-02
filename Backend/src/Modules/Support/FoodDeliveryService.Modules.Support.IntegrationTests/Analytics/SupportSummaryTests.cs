using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using FoodDeliveryService.Modules.Support.Application.Analytics.GetSupportSummary;
using FoodDeliveryService.Modules.Support.Domain.Tickets;
using FoodDeliveryService.Modules.Support.Infrastructure.Database;
using FoodDeliveryService.Modules.Support.IntegrationTests.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Support.IntegrationTests.Analytics;

/// <summary>
/// Drives <c>GET support/analytics/summary</c> against real Postgres and Redis containers.
/// <para>
/// Every test works in a window of its own, years in the past. The suite shares one database across
/// its collection, so a summary over "the last 30 days" would be computed over whatever every other
/// test class happened to have opened — the numbers here are hand-computed, and that only means
/// something if nothing else can land in the window.
/// </para>
/// <para>
/// Tickets are opened through the real endpoint and then back-dated with one UPDATE. Only the
/// timestamps are written directly: everything the assertions are about (the reference, the
/// category, the ownership) still came through the aggregate. There is no other way to place a
/// ticket in 2001 — <c>IDateTimeProvider</c> is resolved inside the host, and faking it would
/// replace the very clock the resolution arithmetic is being tested on.
/// </para>
/// </summary>
public class SupportSummaryTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private const string SummaryEndpoint = "support/analytics/summary";

    [Fact]
    public async Task Summary_ShouldReportCountsAverageAndMedian_MatchingHandComputedValues()
    {
        // Arrange — a window nothing else touches, and three resolutions whose mean and median
        // deliberately differ: 1 h, 2 h and 6 h give a mean of 3 h and a median of 2 h. A single
        // slow case dragging the average away from the typical one is the whole reason both are
        // reported, so the fixture has to contain one.
        var from = new DateTime(2001, 3, 5, 0, 0, 0, DateTimeKind.Utc);
        DateTime to = from.AddDays(7);
        DateTime openedAt = from.AddDays(1);

        using HttpClient customer = await CreateCustomerClientAsync();
        using HttpClient agent = await CreateAgentClientAsync();

        Guid fast = await OpenTicketAsync(customer, "Cold food", "FoodQuality");
        Guid medium = await OpenTicketAsync(customer, "Missing drink", "ItemMissing");
        Guid slow = await OpenTicketAsync(customer, "Never arrived", "OrderNotReceived");
        Guid stillOpen = await OpenTicketAsync(customer, "App crashes", "AppIssue");

        await BackdateAsync(fast, openedAt, openedAt.AddHours(1), firstRespondedOnUtc: openedAt.AddMinutes(10));
        await BackdateAsync(medium, openedAt, openedAt.AddHours(2), firstRespondedOnUtc: openedAt.AddMinutes(30));
        await BackdateAsync(slow, openedAt, openedAt.AddHours(6), firstRespondedOnUtc: openedAt.AddMinutes(50));
        await BackdateAsync(stillOpen, openedAt, resolvedOnUtc: null, firstRespondedOnUtc: null);

        // Act
        SupportSummaryResponse summary = await GetSummaryAsync(agent, from, to);

        // Assert — the window is echoed back so a chart can label its own axis.
        summary.FromUtc.Should().Be(from);
        summary.ToUtc.Should().Be(to);

        summary.Totals.TicketsOpened.Should().Be(4);
        summary.Totals.TicketsResolved.Should().Be(3);
        summary.Totals.AverageResolutionSeconds.Should().Be(3 * 3600d);
        summary.Totals.MedianResolutionSeconds.Should().Be(2 * 3600d);

        // 10 + 30 + 50 minutes over three tickets: mean 30 minutes, median 30 minutes. The
        // still-open ticket has never been replied to and must not count as a zero.
        summary.Totals.TicketsFirstResponded.Should().Be(3);
        summary.Totals.AverageFirstResponseSeconds.Should().Be(30 * 60d);
        summary.Totals.MedianFirstResponseSeconds.Should().Be(30 * 60d);

        // Category and status breakdowns over the same four tickets.
        summary.ByCategory.Should().HaveCount(4);
        summary.ByCategory.Should().ContainSingle(c => c.Category == TicketCategory.OrderNotReceived)
            .Which.Resolved.Should().Be(1);
        summary.ByCategory.Should().ContainSingle(c => c.Category == TicketCategory.AppIssue)
            .Which.Resolved.Should().Be(0);

        summary.ByStatus.Sum(s => s.Count).Should().Be(4);
        summary.ByStatus.Should().ContainSingle(s => s.Status == TicketStatus.Resolved)
            .Which.Count.Should().Be(3);
        summary.ByStatus.Should().ContainSingle(s => s.Status == TicketStatus.Open)
            .Which.Count.Should().Be(1);
    }

    [Fact]
    public async Task Summary_ShouldGapFillAQuietDay_WithAZeroRowRatherThanNoRow()
    {
        // Arrange — activity on the first and third day of a three-day window, nothing on the
        // second. A GROUP BY over the tickets alone returns two rows and a chart drawn from it
        // joins them with a straight line, which reads as steady traffic across a day that had none.
        var from = new DateTime(2002, 6, 10, 0, 0, 0, DateTimeKind.Utc);
        DateTime to = from.AddDays(3);

        using HttpClient customer = await CreateCustomerClientAsync();
        using HttpClient agent = await CreateAgentClientAsync();

        Guid first = await OpenTicketAsync(customer, "Day one", "Other");
        Guid third = await OpenTicketAsync(customer, "Day three", "Other");

        await BackdateAsync(first, from.AddHours(9), resolvedOnUtc: null, firstRespondedOnUtc: null);
        await BackdateAsync(third, from.AddDays(2).AddHours(9), resolvedOnUtc: null, firstRespondedOnUtc: null);

        // Act
        SupportSummaryResponse summary = await GetSummaryAsync(agent, from, to);

        // Assert — one row per day of the window, in order, including the empty middle one.
        summary.TicketsPerDay.Should().HaveCount(3);
        summary.TicketsPerDay.Select(d => d.Date.Date)
            .Should().ContainInOrder(from.Date, from.AddDays(1).Date, from.AddDays(2).Date);

        summary.TicketsPerDay.Select(d => d.Opened).Should().ContainInOrder(1, 0, 1);
    }

    [Fact]
    public async Task Summary_ShouldBeServedFromCache_ForARepeatedCallInsideTheTtl()
    {
        // Arrange — the TTL is the whole freshness contract for this read: it is a platform-wide
        // aggregate with no invalidation, deliberately (SupportCacheKeys.SummaryExpiration). This
        // asserts that decision rather than merely tolerating it.
        var from = new DateTime(2003, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime to = from.AddDays(2);

        using HttpClient customer = await CreateCustomerClientAsync();
        using HttpClient agent = await CreateAgentClientAsync();

        Guid seeded = await OpenTicketAsync(customer, "Before the read", "Other");
        await BackdateAsync(seeded, from.AddHours(4), resolvedOnUtc: null, firstRespondedOnUtc: null);

        SupportSummaryResponse first = await GetSummaryAsync(agent, from, to);
        first.Totals.TicketsOpened.Should().Be(1);

        // Act — a write that lands squarely inside the window between the two reads.
        Guid afterwards = await OpenTicketAsync(customer, "After the read", "Other");
        await BackdateAsync(afterwards, from.AddHours(5), resolvedOnUtc: null, firstRespondedOnUtc: null);

        SupportSummaryResponse second = await GetSummaryAsync(agent, from, to);

        // Assert — unchanged, because the second call never reached Postgres. The bounds are
        // truncated to the minute by GetSupportSummaryQuery.Create, which is what lets two calls
        // share a key at all.
        second.Totals.TicketsOpened.Should().Be(1);
        second.Should().BeEquivalentTo(first);
    }

    [Fact]
    public async Task Summary_ShouldReturnForbidden_ForACustomer()
    {
        // Arrange
        using HttpClient customer = await CreateCustomerClientAsync();

        // Act
        HttpResponseMessage response = await customer.GetAsync(
            new Uri(SummaryEndpoint, UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — 403 and not 404: unlike a ticket read there is no ownership question here to
        // hide behind. The numbers are platform-wide, and a customer simply holds no code that
        // reaches them.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Summary_ShouldReturnBadRequest_ForAnInvertedWindow()
    {
        // Arrange
        using HttpClient agent = await CreateAgentClientAsync();

        var from = new DateTime(2004, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        // Act
        HttpResponseMessage response = await agent.GetAsync(
            new Uri(QueryString(SummaryEndpoint, from, from.AddDays(-1)), UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — the validator, not a generate_series that would silently return no days.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<SupportSummaryResponse> GetSummaryAsync(
        HttpClient client,
        DateTime from,
        DateTime to)
    {
        HttpResponseMessage response = await client.GetAsync(
            new Uri(QueryString(SummaryEndpoint, from, to), UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<SupportSummaryResponse>(
            TestContext.Current.CancellationToken))!;
    }

    private static string QueryString(string endpoint, DateTime from, DateTime to) =>
        string.Create(CultureInfo.InvariantCulture, $"{endpoint}?from={from:O}&to={to:O}");

    /// <summary>
    /// Moves a ticket's three analytics timestamps, and its status with them, so the row is a
    /// consistent one rather than a resolved ticket still reading as Open.
    /// </summary>
    private async Task BackdateAsync(
        Guid ticketId,
        DateTime openedOnUtc,
        DateTime? resolvedOnUtc = null,
        DateTime? firstRespondedOnUtc = null)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<SupportDbContext>();

        int status = (int)(resolvedOnUtc is null ? TicketStatus.Open : TicketStatus.Resolved);

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE tickets
             SET opened_on_utc = {openedOnUtc},
                 resolved_on_utc = {resolvedOnUtc},
                 first_responded_on_utc = {firstRespondedOnUtc},
                 status = {status}
             WHERE id = {ticketId}
             """,
            TestContext.Current.CancellationToken);
    }
}
