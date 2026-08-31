using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Domain.Notifications;
using FoodDeliveryService.Modules.Notifications.Infrastructure.Database;
using FoodDeliveryService.Modules.Support.Application.Tickets.GetTicketMessages;
using FoodDeliveryService.Modules.Support.Domain.Tickets;
using FoodDeliveryService.Modules.Support.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Support.Presentation.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Support.IntegrationTests.Tickets;

/// <summary>
/// The ticket thread over the real pipeline. Two claims are worth a Testcontainers run rather than a
/// unit test: that an internal note is invisible to the customer through the actual SQL the endpoint
/// executes, and that only a customer-visible agent reply crosses the broker into Notifications —
/// which is a fact about two services and cannot be established inside either one.
/// </summary>
public class TicketMessageTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task PostMessage_ShouldReturnOk_WhenCustomerRepliesOnOwnTicket()
    {
        // Arrange
        HttpClient customerClient = await CreateCustomerClientAsync();
        Guid ticketId = await OpenTicketAsync(customerClient);

        // Act
        HttpResponseMessage response = await PostMessageAsync(customerClient, ticketId, "Any update on this?");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        Guid messageId = await response.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);
        messageId.Should().NotBeEmpty();

        IReadOnlyCollection<TicketMessageResponse> thread = await GetMessagesAsync(customerClient, ticketId);

        thread.Should().ContainSingle();
        thread.Single().Id.Should().Be(messageId);
        thread.Single().AuthorKind.Should().Be(TicketAuthorKind.Customer);
        thread.Single().AuthorId.Should().Be(Factory.CustomerUserId);
    }

    [Fact]
    public async Task GetMessages_ShouldHideInternalNoteFromCustomer_AndShowItToAgent()
    {
        // Arrange
        HttpClient customerClient = await CreateCustomerClientAsync();
        HttpClient agentClient = await CreateAgentClientAsync();

        Guid ticketId = await OpenTicketAsync(customerClient);

        const string reply = "We are refunding the order now";
        const string note = "Restaurant confirmed the courier never collected";

        (await PostMessageAsync(agentClient, ticketId, reply)).EnsureSuccessStatusCode();

        (await PostMessageAsync(agentClient, ticketId, note, nameof(TicketMessageVisibility.InternalNote)))
            .EnsureSuccessStatusCode();

        // Act
        IReadOnlyCollection<TicketMessageResponse> customerThread = await GetMessagesAsync(customerClient, ticketId);
        IReadOnlyCollection<TicketMessageResponse> agentThread = await GetMessagesAsync(agentClient, ticketId);

        // Assert — the note is excluded by the query's WHERE clause, so it is not merely unrendered:
        // there is no code path on which a customer-facing projection has it in hand at all.
        customerThread.Should().ContainSingle();
        customerThread.Single().Body.Should().Be(reply);
        customerThread.Should().NotContain(m => m.Visibility == TicketMessageVisibility.InternalNote);

        agentThread.Should().HaveCount(2);
        agentThread.Should().ContainSingle(m => m.Body == note);
    }

    [Fact]
    public async Task PostMessage_ShouldReturnBadRequest_WhenCustomerAsksForInternalNote()
    {
        // Arrange
        HttpClient customerClient = await CreateCustomerClientAsync();
        Guid ticketId = await OpenTicketAsync(customerClient);

        // Act
        HttpResponseMessage response = await PostMessageAsync(
            customerClient,
            ticketId,
            "Hiding this from myself",
            nameof(TicketMessageVisibility.InternalNote));

        // Assert — refused, not silently downgraded to a customer-visible message.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        IReadOnlyCollection<TicketMessageResponse> thread = await GetMessagesAsync(customerClient, ticketId);
        thread.Should().BeEmpty();
    }

    [Fact]
    public async Task PostMessage_ShouldReturnNotFound_WhenTicketBelongsToAnotherCustomer()
    {
        // Arrange
        HttpClient customerClient = await CreateCustomerClientAsync();
        HttpClient otherCustomerClient = await CreateOtherCustomerClientAsync();

        Guid ticketId = await OpenTicketAsync(customerClient);

        // Act
        HttpResponseMessage response = await PostMessageAsync(otherCustomerClient, ticketId, "Let me in");

        // Assert — 404 rather than 403: a forbidden would confirm that the ticket exists, which is
        // precisely what a customer enumerating ticket ids is trying to learn.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostMessage_CustomerVisibleAgentReply_ShouldNotifyTheCustomer()
    {
        // Arrange — a unique subject makes the notification row this test produced identifiable
        // among every other row in the shared Notifications database.
        HttpClient customerClient = await CreateCustomerClientAsync();
        HttpClient agentClient = await CreateAgentClientAsync();

        string subject = $"Reply notification {Guid.NewGuid():N}";
        Guid ticketId = await OpenTicketAsync(customerClient, subject);

        // Act
        (await PostMessageAsync(agentClient, ticketId, "Your refund is on its way")).EnsureSuccessStatusCode();

        // Assert — Support's outbox publishes, the broker delivers, the Notifications inbox consumes
        // and the send is logged in that service's own database. Sixty seconds because the path
        // crosses two poll intervals and a container-hosted broker.
        Result<Notification> notification = await WaitForNotificationAsync(subject, TimeSpan.FromSeconds(60));

        notification.IsSuccess.Should().BeTrue("an agent's customer-visible reply should email the customer");

        Notification row = notification.Value;
        row.Type.Should().Be(NotificationType.SupportTicketReply);
        row.Channel.Should().Be(NotificationChannel.Email);
        row.RecipientUserId.Should().Be(Factory.CustomerUserId);
        row.Status.Should().Be(NotificationStatus.Sent);
    }

    [Fact]
    public async Task PostMessage_InternalNote_ShouldNotNotifyTheCustomer()
    {
        // Arrange
        HttpClient customerClient = await CreateCustomerClientAsync();
        HttpClient agentClient = await CreateAgentClientAsync();

        string subject = $"Internal note {Guid.NewGuid():N}";
        Guid ticketId = await OpenTicketAsync(customerClient, subject);

        // Act
        (await PostMessageAsync(
                agentClient,
                ticketId,
                "Do not tell the customer we lost the order",
                nameof(TicketMessageVisibility.InternalNote)))
            .EnsureSuccessStatusCode();

        // Assert — the poller only succeeds when a row appears, so a timeout is the pass signal. The
        // filter under test is in Support: an internal note never reaches the bus, which is the only
        // version of "it did not leak" that a mistake downstream could still fail.
        Result<Notification> notification = await WaitForNotificationAsync(subject, TimeSpan.FromSeconds(20));

        notification.IsFailure.Should().BeTrue("an internal note must never leave the Support service");
    }

    private static async Task<HttpResponseMessage> PostMessageAsync(
        HttpClient client,
        Guid ticketId,
        string body,
        string visibility = nameof(TicketMessageVisibility.CustomerVisible)) =>
        await client.PostAsJsonAsync(
            $"support/tickets/{ticketId}/messages",
            new PostTicketMessage.Request { Body = body, Visibility = visibility },
            TestContext.Current.CancellationToken);

    private static async Task<IReadOnlyCollection<TicketMessageResponse>> GetMessagesAsync(
        HttpClient client,
        Guid ticketId)
    {
        HttpResponseMessage response = await client.GetAsync(
            new Uri($"support/tickets/{ticketId}/messages", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<List<TicketMessageResponse>>(
            TestContext.Current.CancellationToken))!;
    }

    /// <summary>
    /// Polls the Notifications host's own database for the row this test's ticket would produce. The
    /// email subject carries the ticket's subject, which is what makes one test's notification
    /// distinguishable from another's in a fixture the whole suite shares.
    /// </summary>
    private async Task<Result<Notification>> WaitForNotificationAsync(string ticketSubject, TimeSpan timeout) =>
        await Poller.WaitAsync<Notification>(
            timeout,
            async () =>
            {
                await using AsyncServiceScope scope = Factory.NotificationsApi.Services.CreateAsyncScope();

                var context = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

                // Notification? → Result<Notification>: null converts to Failure(Error.NullValue),
                // which keeps the poller retrying until the send is logged.
                return await context.Set<Notification>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        n => n.Subject.Contains(ticketSubject),
                        TestContext.Current.CancellationToken);
            });
}
