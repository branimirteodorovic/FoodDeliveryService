using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Application.Tickets.GetTicket;
using FoodDeliveryService.Modules.Support.Application.Tickets.GetTicketAudit;
using FoodDeliveryService.Modules.Support.Domain.Audit;
using FoodDeliveryService.Modules.Support.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Support.Presentation.Tickets;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Support.IntegrationTests.Tickets;

/// <summary>
/// Assignment and the audit log through the full pipeline: real Duende JWTs, the real permission
/// RPC answered by an in-process Users host, and real Postgres/Redis/RabbitMQ containers.
/// <para>
/// The Redis container is what makes the concurrency test here worth anything — the claim lock is a
/// real <c>SET NX PX</c> against it, so two simultaneous claims are genuinely serialized rather than
/// being serialized by an in-memory fallback that would pass whether or not the code was correct.
/// </para>
/// </summary>
public class TicketAssignmentTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task ClaimTicket_ShouldAssignTheCallingAgent_AndWriteOneAuditEntry()
    {
        // Arrange
        using HttpClient customer = await CreateCustomerClientAsync();
        using HttpClient agent = await CreateAgentClientAsync();

        Guid ticketId = await OpenTicketAsync(customer);

        // Act
        HttpResponseMessage response = await agent.PostAsync(
            new Uri($"support/tickets/{ticketId}/claim", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        TicketResponse? ticket = await agent.GetFromJsonAsync<TicketResponse>(
            $"support/tickets/{ticketId}",
            TestContext.Current.CancellationToken);

        ticket.Should().NotBeNull();
        ticket!.AssignedAgentId.Should().Be(Factory.AgentUserId);

        // Claiming says who owns the ticket, not that work has started.
        ticket.Status.Should().Be(Domain.Tickets.TicketStatus.Open);

        IReadOnlyCollection<SupportAuditEntryResponse> audit = await GetAuditAsync(agent, ticketId);

        audit.Should().ContainSingle();
        audit.Single().Action.Should().Be(SupportAuditAction.Claimed);
        audit.Single().ActorId.Should().Be(Factory.AgentUserId);
        audit.Single().ToValue.Should().Be(Factory.AgentUserId.ToString());
    }

    [Fact]
    public async Task ClaimTicket_ShouldLetExactlyOneAgentWin_WhenTwoClaimConcurrently()
    {
        // Arrange
        using HttpClient customer = await CreateCustomerClientAsync();
        using HttpClient firstAgent = await CreateAgentClientAsync();
        using HttpClient secondAgent = await CreateOtherAgentClientAsync();

        Guid ticketId = await OpenTicketAsync(customer);

        // Act — genuinely concurrent, both against the same ticket. The response is read and
        // disposed inside the local function rather than being handed back as a live
        // HttpResponseMessage, so nothing disposable escapes into an unawaited task.
        // Awaited in the same statement that starts them, so the analyzer can see that neither task
        // outlives the HttpClient it was handed.
        (HttpStatusCode Status, string Body)[] outcomes = await Task.WhenAll(
            ClaimAndReadAsync(firstAgent, ticketId),
            ClaimAndReadAsync(secondAgent, ticketId));

        // Assert — one winner, and the loser fails cleanly rather than throwing or hanging. It may
        // lose at either guard: the lock (ClaimInProgress) if it arrived while the winner held it,
        // or the aggregate (AlreadyAssigned) if it arrived after the winner committed. Both are
        // correct, and pinning the test to one would make it a test of timing rather than of
        // correctness.
        outcomes.Count(o => o.Status == HttpStatusCode.NoContent).Should().Be(1);

        (HttpStatusCode Status, string Body) loser = outcomes.Single(o => o.Status != HttpStatusCode.NoContent);

        loser.Body.Should().MatchRegex("Tickets.(ClaimInProgress|AlreadyAssigned)");

        // The assertion that actually proves the lock. Two responses could look exactly like this
        // even with both writes landing — the second silently overwriting the first — but exactly
        // one Claimed audit row cannot, because each successful claim writes one in its own
        // transaction.
        IReadOnlyCollection<SupportAuditEntryResponse> audit = await GetAuditAsync(firstAgent, ticketId);

        audit.Count(e => e.Action == SupportAuditAction.Claimed).Should().Be(1);
    }

    [Fact]
    public async Task ClaimTicket_ShouldConflict_WhenTheTicketIsAlreadyAssigned()
    {
        // Arrange
        using HttpClient customer = await CreateCustomerClientAsync();
        using HttpClient firstAgent = await CreateAgentClientAsync();
        using HttpClient secondAgent = await CreateOtherAgentClientAsync();

        Guid ticketId = await OpenTicketAsync(customer);

        await ClaimTicketAsync(firstAgent, ticketId);

        // Act
        HttpResponseMessage response = await secondAgent.PostAsync(
            new Uri($"support/tickets/{ticketId}/claim", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.Should().Contain("Tickets.AlreadyAssigned");
    }

    [Fact]
    public async Task ClaimTicket_ShouldForbidACustomer()
    {
        // Arrange — a customer holds support-tickets:open and :read, never :assign.
        using HttpClient customer = await CreateCustomerClientAsync();

        Guid ticketId = await OpenTicketAsync(customer);

        // Act
        HttpResponseMessage response = await customer.PostAsync(
            new Uri($"support/tickets/{ticketId}/claim", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AssignTicket_ShouldRefuseAnAgentNamingAnotherAgent()
    {
        // Arrange — both agents hold support-tickets:assign, so the route policy admits the request.
        // What stops it is the administrator bypass check inside the handler.
        using HttpClient customer = await CreateCustomerClientAsync();
        using HttpClient agent = await CreateAgentClientAsync();

        Guid ticketId = await OpenTicketAsync(customer);

        // Act
        HttpResponseMessage response = await agent.PostAsJsonAsync(
            $"support/tickets/{ticketId}/assign",
            new AssignTicket.Request { AgentId = Factory.OtherAgentUserId, Reason = "You take it" },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.Should().Contain("Support.NotAuthorizedToAssignAnotherAgent");
    }

    [Fact]
    public async Task AssignTicket_ShouldReassign_ForAnAdministrator()
    {
        // Arrange — the whole administrator override, end to end: an agent has the ticket, and an
        // administrator moves it to somebody else. The assignment target has to exist in Support's
        // own agent replica, which is populated only by the Users registration event travelling the
        // real broker — so this also proves the replica projection works.
        using HttpClient customer = await CreateCustomerClientAsync();
        using HttpClient agent = await CreateAgentClientAsync();
        using HttpClient admin = await CreateAdminClientAsync();

        await WaitForAgentReplicaAsync(Factory.OtherAgentUserId);

        Guid ticketId = await OpenTicketAsync(customer);

        await ClaimTicketAsync(agent, ticketId);

        // Act
        HttpResponseMessage response = await admin.PostAsJsonAsync(
            $"support/tickets/{ticketId}/assign",
            new AssignTicket.Request { AgentId = Factory.OtherAgentUserId, Reason = "Rebalancing the queue" },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        TicketResponse? ticket = await admin.GetFromJsonAsync<TicketResponse>(
            $"support/tickets/{ticketId}",
            TestContext.Current.CancellationToken);

        ticket.Should().NotBeNull();
        ticket!.AssignedAgentId.Should().Be(Factory.OtherAgentUserId);

        IReadOnlyCollection<SupportAuditEntryResponse> audit = await GetAuditAsync(admin, ticketId);

        SupportAuditEntryResponse assigned = audit.Single(e => e.Action == SupportAuditAction.Assigned);

        // Both halves of the reassignment: who lost the ticket and who gained it.
        assigned.FromValue.Should().Be(Factory.AgentUserId.ToString());
        assigned.ToValue.Should().Be(Factory.OtherAgentUserId.ToString());
        assigned.Reason.Should().Be("Rebalancing the queue");
    }

    [Fact]
    public async Task AssignTicket_ShouldReturnNotFound_ForAUserWhoIsNotASupportAgent()
    {
        // Arrange — a customer id is a perfectly well-formed Guid that names nobody in the agent
        // replica. Without this check the ticket would end up owned by somebody who cannot see it,
        // and invisible to the unassigned queue filter that would otherwise surface it again.
        using HttpClient customer = await CreateCustomerClientAsync();
        using HttpClient admin = await CreateAdminClientAsync();

        Guid ticketId = await OpenTicketAsync(customer);

        // Act
        HttpResponseMessage response = await admin.PostAsJsonAsync(
            $"support/tickets/{ticketId}/assign",
            new AssignTicket.Request { AgentId = Guid.NewGuid() },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.Should().Contain("Support.AgentNotFound");
    }

    [Fact]
    public async Task UnassignTicket_ShouldReturnTheTicketToTheQueue_AndAuditTheReason()
    {
        // Arrange
        using HttpClient customer = await CreateCustomerClientAsync();
        using HttpClient agent = await CreateAgentClientAsync();

        Guid ticketId = await OpenTicketAsync(customer);

        await ClaimTicketAsync(agent, ticketId);

        // Act
        HttpResponseMessage response = await agent.PostAsJsonAsync(
            $"support/tickets/{ticketId}/unassign",
            new UnassignTicket.Request { Reason = "Wrong specialism" },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        TicketResponse? ticket = await agent.GetFromJsonAsync<TicketResponse>(
            $"support/tickets/{ticketId}",
            TestContext.Current.CancellationToken);

        ticket.Should().NotBeNull();
        ticket!.AssignedAgentId.Should().BeNull();

        IReadOnlyCollection<SupportAuditEntryResponse> audit = await GetAuditAsync(agent, ticketId);

        SupportAuditEntryResponse unassigned = audit.Single(e => e.Action == SupportAuditAction.Unassigned);

        unassigned.FromValue.Should().Be(Factory.AgentUserId.ToString());
        unassigned.ToValue.Should().BeNull();
        unassigned.Reason.Should().Be("Wrong specialism");
    }

    [Fact]
    public async Task UnassignTicket_ShouldReturnBadRequest_WithoutAReason()
    {
        // Arrange
        using HttpClient customer = await CreateCustomerClientAsync();
        using HttpClient agent = await CreateAgentClientAsync();

        Guid ticketId = await OpenTicketAsync(customer);

        await ClaimTicketAsync(agent, ticketId);

        // Act
        HttpResponseMessage response = await agent.PostAsJsonAsync(
            $"support/tickets/{ticketId}/unassign",
            new UnassignTicket.Request { Reason = string.Empty },
            TestContext.Current.CancellationToken);

        // Assert — caught by the validator before the aggregate, which also guards it.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetTicketAudit_ShouldRecordAStatusChange_WithItsBeforeAndAfter()
    {
        // Arrange — the Milestone B status endpoint, retrofitted to write an audit entry. Escalated
        // is the transition an unclaimed ticket can make, which keeps this test about the audit
        // entry rather than about assignment.
        using HttpClient customer = await CreateCustomerClientAsync();
        using HttpClient agent = await CreateAgentClientAsync();

        Guid ticketId = await OpenTicketAsync(customer);

        HttpResponseMessage statusResponse = await agent.PostAsJsonAsync(
            $"support/tickets/{ticketId}/status",
            new ChangeTicketStatus.Request { Status = "Escalated", Reason = "Needs a supervisor" },
            TestContext.Current.CancellationToken);

        statusResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Act
        IReadOnlyCollection<SupportAuditEntryResponse> audit = await GetAuditAsync(agent, ticketId);

        // Assert
        SupportAuditEntryResponse entry = audit.Single(e => e.Action == SupportAuditAction.StatusChanged);

        entry.FromValue.Should().Be(nameof(Domain.Tickets.TicketStatus.Open));
        entry.ToValue.Should().Be(nameof(Domain.Tickets.TicketStatus.Escalated));
        entry.Reason.Should().Be("Needs a supervisor");
        entry.ActorId.Should().Be(Factory.AgentUserId);
        entry.OccurredOnUtc.Should().NotBe(default);

        // The name comes from the local agent replica, so this also proves the projection reached
        // the read side rather than only the assignment guard.
        entry.ActorName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetTicketAudit_ShouldReturnEntriesNewestFirst()
    {
        // Arrange
        using HttpClient customer = await CreateCustomerClientAsync();
        using HttpClient agent = await CreateAgentClientAsync();

        Guid ticketId = await OpenTicketAsync(customer);

        await ClaimTicketAsync(agent, ticketId);

        HttpResponseMessage statusResponse = await agent.PostAsJsonAsync(
            $"support/tickets/{ticketId}/status",
            new ChangeTicketStatus.Request { Status = "InProgress" },
            TestContext.Current.CancellationToken);

        statusResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Act
        IReadOnlyCollection<SupportAuditEntryResponse> audit = await GetAuditAsync(agent, ticketId);

        // Assert
        audit.Should().HaveCount(2);
        audit.Should().BeInDescendingOrder(e => e.OccurredOnUtc);
        audit.First().Action.Should().Be(SupportAuditAction.StatusChanged);
    }

    [Fact]
    public async Task GetTicketAudit_ShouldBeEmpty_ForATicketNothingHasHappenedTo()
    {
        // Arrange — an untouched ticket has an empty history, which is a different answer from a
        // ticket that does not exist. Inferring the 404 from an empty list would conflate them.
        using HttpClient customer = await CreateCustomerClientAsync();
        using HttpClient agent = await CreateAgentClientAsync();

        Guid ticketId = await OpenTicketAsync(customer);

        // Act
        HttpResponseMessage response = await agent.GetAsync(
            new Uri($"support/tickets/{ticketId}/audit", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        IReadOnlyCollection<SupportAuditEntryResponse>? audit =
            await response.Content.ReadFromJsonAsync<IReadOnlyCollection<SupportAuditEntryResponse>>(
                TestContext.Current.CancellationToken);

        audit.Should().NotBeNull();
        audit!.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTicketAudit_ShouldForbidACustomer_EvenOnTheirOwnTicket()
    {
        // Arrange — the one ticket read that is not ownership-scoped but flatly staff-only: the
        // entries carry the internal reasons agents write for each other.
        using HttpClient customer = await CreateCustomerClientAsync();

        Guid ticketId = await OpenTicketAsync(customer);

        // Act
        HttpResponseMessage response = await customer.GetAsync(
            new Uri($"support/tickets/{ticketId}/audit", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — 403 rather than 404 here, and deliberately so: the customer already knows this
        // ticket exists, so there is nothing to hide by pretending otherwise.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SupportAgentReplica_ShouldHoldStaffOnly()
    {
        // Arrange & Act — the replica is fed by UserRegisteredIntegrationEvent over the real broker,
        // published by the in-process Users host when the fixture seeded its users.
        await WaitForAgentReplicaAsync(Factory.AgentUserId);

        // Assert — and a customer, registered through exactly the same event, is not in it. The
        // handler filters on the role snapshot the event carries; this table is the set of people a
        // ticket can be assigned to, not a second copy of the user directory.
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        var dbConnectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        const string sql = "SELECT COUNT(*) FROM support_agents WHERE id = @UserId";

        using HttpClient customer = await CreateCustomerClientAsync();
        Guid customerTicketId = await OpenTicketAsync(customer);

        TicketResponse? customerTicket = await customer.GetFromJsonAsync<TicketResponse>(
            $"support/tickets/{customerTicketId}",
            TestContext.Current.CancellationToken);

        int customerRows = await connection.ExecuteScalarAsync<int>(
            sql,
            new { UserId = customerTicket!.CustomerId });

        customerRows.Should().Be(0);
    }

    private static async Task<(HttpStatusCode Status, string Body)> ClaimAndReadAsync(
        HttpClient client,
        Guid ticketId)
    {
        using HttpResponseMessage response = await client.PostAsync(
            new Uri($"support/tickets/{ticketId}/claim", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        return (response.StatusCode, body);
    }

    private static async Task ClaimTicketAsync(HttpClient client, Guid ticketId)
    {
        using HttpResponseMessage response = await client.PostAsync(
            new Uri($"support/tickets/{ticketId}/claim", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static async Task<IReadOnlyCollection<SupportAuditEntryResponse>> GetAuditAsync(
        HttpClient client,
        Guid ticketId)
    {
        IReadOnlyCollection<SupportAuditEntryResponse>? audit =
            await client.GetFromJsonAsync<IReadOnlyCollection<SupportAuditEntryResponse>>(
                $"support/tickets/{ticketId}/audit",
                TestContext.Current.CancellationToken);

        audit.Should().NotBeNull();

        return audit!;
    }

    /// <summary>
    /// Waits for the agent replica row to arrive. It travels Users outbox → RabbitMQ → Support inbox
    /// → ProcessInboxJob, so it is eventually consistent by construction — asserting on it without
    /// waiting would be a race, not a test.
    /// </summary>
    private async Task WaitForAgentReplicaAsync(Guid agentId)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        var dbConnectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

        Result<int> found = await Poller.WaitAsync(TimeSpan.FromSeconds(30), async () =>
        {
            await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

            int count = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM support_agents WHERE id = @AgentId",
                new { AgentId = agentId });

            return count > 0
                ? Result.Success(count)
                : Result.Failure<int>(
                    Error.NotFound("SupportAgents.NotFound", "The agent replica row has not arrived yet"));
        });

        found.IsSuccess.Should().BeTrue("the support agent replica is projected from UserRegistered");
    }
}
