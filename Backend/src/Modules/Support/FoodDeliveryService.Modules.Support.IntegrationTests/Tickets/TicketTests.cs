using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Application.Tickets.GetTicket;
using FoodDeliveryService.Modules.Support.Application.Tickets.GetTickets;
using FoodDeliveryService.Modules.Support.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Support.Presentation.Tickets;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Support.IntegrationTests.Tickets;

/// <summary>
/// Drives the four Milestone B endpoints through the full pipeline — real Duende JWTs, the real
/// permission RPC answered by an in-process Users host, real Postgres/Redis/RabbitMQ containers.
/// The authorization assertions are the reason for all of that: a 403 here is the seeded permission
/// set actually resolving, not a stub agreeing with the test.
/// </summary>
public class TicketTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task OpenTicket_ShouldPersistTheTicket_AndLetTheCustomerReadItBack()
    {
        // Arrange
        using HttpClient customer = await CreateCustomerClientAsync();

        // Act
        Guid ticketId = await OpenTicketAsync(customer, "My order never arrived", "OrderNotReceived");

        HttpResponseMessage response = await customer.GetAsync(
            new Uri($"support/tickets/{ticketId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        TicketResponse? ticket = await response.Content.ReadFromJsonAsync<TicketResponse>(
            TestContext.Current.CancellationToken);

        ticket.Should().NotBeNull();
        ticket!.Id.Should().Be(ticketId);
        ticket.Subject.Should().Be("My order never arrived");
        ticket.Status.Should().Be(Domain.Tickets.TicketStatus.Open);
        ticket.AssignedAgentId.Should().BeNull();

        // The customer id came from the token, never from the body.
        ticket.CustomerId.Should().NotBeEmpty();

        // The OrderNotReceived priority rule, surviving the round-trip through the database.
        ticket.Priority.Should().Be(Domain.Tickets.TicketPriority.High);

        // Human-quotable and allocated from the sequence.
        ticket.Reference.Should().StartWith("SUP-");
        ticket.Reference.Should().HaveLength(12);
    }

    [Fact]
    public async Task GetTicket_ShouldReturnNotFound_ForAnotherCustomersTicket()
    {
        // Arrange
        using HttpClient owner = await CreateCustomerClientAsync();
        using HttpClient stranger = await CreateOtherCustomerClientAsync();

        Guid ticketId = await OpenTicketAsync(owner);

        // Act
        HttpResponseMessage response = await stranger.GetAsync(
            new Uri($"support/tickets/{ticketId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — 404 and not 403 on purpose: a 403 confirms the ticket exists, which is exactly
        // what a customer probing other customers' ticket ids is trying to learn.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetTickets_ShouldReturnOnlyTheCallersOwnTickets_ForACustomer()
    {
        // Arrange
        using HttpClient owner = await CreateCustomerClientAsync();
        using HttpClient stranger = await CreateOtherCustomerClientAsync();

        Guid ownTicketId = await OpenTicketAsync(owner);
        Guid strangerTicketId = await OpenTicketAsync(stranger);

        // Act
        IReadOnlyCollection<TicketSummaryResponse>? tickets =
            await owner.GetFromJsonAsync<IReadOnlyCollection<TicketSummaryResponse>>(
                "support/tickets?pageSize=100",
                TestContext.Current.CancellationToken);

        // Assert
        tickets.Should().NotBeNull();
        tickets!.Select(t => t.Id).Should().Contain(ownTicketId);
        tickets.Select(t => t.Id).Should().NotContain(strangerTicketId);
    }

    [Fact]
    public async Task GetTickets_ShouldReturnEveryCustomersTickets_ForAnAgent()
    {
        // Arrange
        using HttpClient customer = await CreateCustomerClientAsync();
        using HttpClient otherCustomer = await CreateOtherCustomerClientAsync();
        using HttpClient agent = await CreateAgentClientAsync();

        Guid firstTicketId = await OpenTicketAsync(customer);
        Guid secondTicketId = await OpenTicketAsync(otherCustomer);

        // Act
        IReadOnlyCollection<TicketSummaryResponse>? tickets =
            await agent.GetFromJsonAsync<IReadOnlyCollection<TicketSummaryResponse>>(
                "support/tickets?pageSize=100",
                TestContext.Current.CancellationToken);

        // Assert
        tickets.Should().NotBeNull();
        tickets!.Select(t => t.Id).Should().Contain([firstTicketId, secondTicketId]);
    }

    [Fact]
    public async Task GetTickets_ShouldNarrowToTheQueue_WhenFilteredOnStatusAndUnassigned()
    {
        // Arrange
        using HttpClient customer = await CreateCustomerClientAsync();
        using HttpClient agent = await CreateAgentClientAsync();

        Guid ticketId = await OpenTicketAsync(customer);

        // Act
        IReadOnlyCollection<TicketSummaryResponse>? queue =
            await agent.GetFromJsonAsync<IReadOnlyCollection<TicketSummaryResponse>>(
                "support/tickets?status=Open&unassigned=true&pageSize=100",
                TestContext.Current.CancellationToken);

        // Assert
        queue.Should().NotBeNull();
        queue!.Select(t => t.Id).Should().Contain(ticketId);
        queue.Should().OnlyContain(t => t.Status == Domain.Tickets.TicketStatus.Open);
        queue.Should().OnlyContain(t => t.AssignedAgentId == null);
    }

    [Fact]
    public async Task ChangeTicketStatus_ShouldForbidACustomer()
    {
        // Arrange — a customer holds support-tickets:open and :read, never :manage.
        using HttpClient customer = await CreateCustomerClientAsync();

        Guid ticketId = await OpenTicketAsync(customer);

        // Act
        HttpResponseMessage response = await customer.PostAsJsonAsync(
            $"support/tickets/{ticketId}/status",
            new ChangeTicketStatus.Request { Status = "Escalated", Reason = "I want a supervisor" },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ChangeTicketStatus_ShouldEscalate_ForAnAgent()
    {
        // Arrange
        using HttpClient customer = await CreateCustomerClientAsync();
        using HttpClient agent = await CreateAgentClientAsync();

        Guid ticketId = await OpenTicketAsync(customer);

        // Act
        HttpResponseMessage response = await agent.PostAsJsonAsync(
            $"support/tickets/{ticketId}/status",
            new ChangeTicketStatus.Request { Status = "Escalated", Reason = "Needs a supervisor" },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        TicketResponse? ticket = await agent.GetFromJsonAsync<TicketResponse>(
            $"support/tickets/{ticketId}",
            TestContext.Current.CancellationToken);

        ticket.Should().NotBeNull();
        ticket!.Status.Should().Be(Domain.Tickets.TicketStatus.Escalated);
    }

    [Fact]
    public async Task ChangeTicketStatus_ShouldReturnBadRequest_ForAnIllegalTransition()
    {
        // Arrange — Open cannot go straight to Resolved; the aggregate says so, and it returns a
        // Result rather than throwing, so this must be a 400 with problem details and not a 500.
        using HttpClient customer = await CreateCustomerClientAsync();
        using HttpClient agent = await CreateAgentClientAsync();

        Guid ticketId = await OpenTicketAsync(customer);

        // Act
        HttpResponseMessage response = await agent.PostAsJsonAsync(
            $"support/tickets/{ticketId}/status",
            new ChangeTicketStatus.Request { Status = "Resolved", Reason = "Sorted" },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.Should().Contain("Tickets.InvalidTransition");
    }

    [Fact]
    public async Task OpenTicket_ShouldFail_WhenACustomerNamesAnotherCustomer()
    {
        // Arrange — the one field that can name somebody else is gated on support-tickets:manage.
        using HttpClient customer = await CreateCustomerClientAsync();

        // Act
        HttpResponseMessage response = await customer.PostAsJsonAsync(
            "support/tickets",
            new OpenTicket.Request
            {
                OnBehalfOfCustomerId = Guid.NewGuid(),
                Subject = "Filed for someone else",
                Category = "Other"
            },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.Should().Contain("Support.NotAuthorizedToActOnBehalfOfCustomer");
    }

    [Fact]
    public async Task OpenTicket_ShouldWriteSupportTicketOpenedToTheOutbox()
    {
        // Arrange
        using HttpClient customer = await CreateCustomerClientAsync();

        // Act
        Guid ticketId = await OpenTicketAsync(customer);

        // Assert — the domain event lands in outbox_messages in the same transaction as the
        // ticket. Polled because the row is written on commit, and read by content because the
        // outbox stores the domain event, which is what ProcessOutboxJob turns into the
        // integration event.
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        var dbConnectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

        Result<int> found = await Poller.WaitAsync(TimeSpan.FromSeconds(15), async () =>
        {
            await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

            const string sql =
                """
                SELECT COUNT(*)
                FROM outbox_messages
                WHERE type = 'TicketOpenedDomainEvent'
                  AND content ->> 'TicketId' = @TicketId
                """;

            int count = await connection.ExecuteScalarAsync<int>(sql, new { TicketId = ticketId.ToString() });

            return count > 0 ? Result.Success(count) : Result.Failure<int>(
                Error.NotFound("Outbox.NotFound", "The TicketOpened message has not been written yet"));
        });

        found.IsSuccess.Should().BeTrue();
        found.Value.Should().Be(1);
    }
}
