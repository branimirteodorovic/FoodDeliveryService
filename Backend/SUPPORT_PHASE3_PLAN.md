# Feature 3.6 — Support Service & Ticketing — Implementation Plan

> Tenth implementation plan, after `RESTAURANTS_PHASE1_PLAN.md`, `ORDERS_PHASE1_PLAN.md`, `NOTIFICATIONS_PHASE1_PLAN.md`, `DELIVERY_PHASE2_PLAN.md`, `REALTIME_PHASE2_PLAN.md`, `CACHING_PHASE2_PLAN.md`, `TELEMETRY_PHASE2_PLAN.md`, `KUBERNETES_PHASE2_PLAN.md` and `LOADTESTING_PHASE3_PLAN.md`. This one covers **Feature 3.6 — Support Service & Ticketing** from `FoodDelivery_ProjectPlan.md`.

> **Scope:** a **new Support service** owning support tickets, their assignment to agents, the agent↔customer message thread, refund *requests* (a record only — there is no payment processing anywhere in this platform), an append-only audit log of every agent action, and a support analytics summary. Backend only — the "support web app" is the Angular workstream (`Frontend/FRONTEND_PLAN.md`); what ships here is the API surface it will consume.

Decisions locked in for this plan:

- **Support is a new service**, `fooddeliveryservice.support.api` on `:5700`, database `fooddeliveryservice_support`, routed at `support/**`. Seventh module, ninth host. It owns `Ticket` (with its message thread), `RefundRequest` and `SupportAuditEntry`, and keeps local replicas of the customer, order and agent data it needs — exactly as Orders and Delivery do.
- **The order history on a ticket comes from a replica, never a cross-service read.** Support subscribes to the eight order/delivery lifecycle events that already exist and projects them into an `OrderSnapshot` + `OrderTimelineEntry` pair. Hard rule #5 (never query another service's tables) is not negotiable, and hard rule #9 means every event already carries the full snapshot needed.
- **Refunds are a request record, full stop.** `RefundRequest` lives in Support, is requested by an agent and approved by an administrator (segregation of duties — a different permission), and publishes an integration event so Notifications can email the customer. Nothing consumes it in Orders, because no money moves. This is exactly what the project plan specifies.
- **Ticket claim and refund approval take the `IDistributedLock`.** Both are textbook check-then-act on state another caller can change (read status → decide → write), and no aggregate in this codebase carries an optimistic concurrency token, so the database will not reject the second write. See `CLAUDE.md` § Distributed Locking.
- **The permission codes are `support-tickets:*`, not `tickets:*`.** `tickets:read` / `tickets:check-in` used to exist in `Permission.cs` as leftovers from the original event-ticketing scaffold, granted to every Customer; reusing them would have silently handed support access to the entire customer base. Milestone A deleted that whole scaffold set (`events:*`, `ticket-types:*`, `categories:*`, `tickets:*`, `event-statistics:read`) — the `support-*` namespace stands on its own.
- Reference implementations to mirror: **Delivery** for a new service skeleton + the distributed lock, **Orders** for replicas fed by integration events, **Notifications** for an audit-log-shaped aggregate and the email channel, **RealTime** for the support dashboard group that already exists.

---

## 0. Prerequisites — what already exists, and what does not

**Already in place — do not rebuild:**

| Thing | Where | Note |
|---|---|---|
| `Role.SupportAgent` | `Users.Domain/Users/Role.cs` | Assignable, admin-provisioned, never self-registered. |
| `Permission.ViewSupportDashboard` (`support:dashboard`) | `Users.Domain/Users/Permission.cs` | Already seeded to `SupportAgent`. |
| The `support` SignalR group + `SupportActivity` hub method + `SupportActivityFrame` | `RealTime.Application/RealTime/` | A global live feed of every order/delivery transition, joined from the `support:dashboard` permission claim. Milestone H extends it; it does not need re-inventing. |
| Admin provisioning of a `SupportAgent` account | `ProvisionUserRequest(Email, First, Last, Role)` → invitation → `accept-invitation` | The generalized RPC from `DELIVERY_PHASE2_PLAN.md` §2.4 already accepts any assignable role. |
| Eight lifecycle integration events | Orders: `OrderPlaced`, `OrderAccepted`, `OrderRejected`, `OrderReadyForPickup`, `OrderCancelled`. Delivery: `DriverAssigned`, `OrderPickedUp`, `OrderDelivered`. | These are the whole timeline. Orders does **not** publish its own `OutForDelivery`/`Delivered` events — those two states are derived from Delivery's events. |

**Explicitly NOT available, and this plan does not pretend otherwise:**

- **The AI chatbot (Feature 3.1/3.2) does not exist.** The project plan's "View the AI chatbot conversation that preceded the human escalation" has no producer. Milestone B adds `Ticket.Source` (with a `Chatbot` member) and a nullable `EscalationTranscript` column so the field is there to fill additively when 3.1 lands — but nothing writes it in this feature. Do not build a fake transcript.
- **The FraudDetection service does not exist.** It was added in `d040427` and **reverted in `6ae4879`** on 2026-08-08; there is no `src/Modules/FraudDetection` in any branch's tree (only stale `bin/obj` output on disk). The project plan's Feature 3.4 task "send high-risk flags as events to the Support Service for manual review" therefore has no producer either, and the fraud dashboard is not in scope. `TicketSource.FraudFlag` is reserved so a future `HighRiskAccountFlaggedIntegrationEvent` consumer is a pure addition; nothing more.
- **No role claim is minted in the JWT.** `FoodDeliveryService.Identity` registers `IdentityRole` but assigns no roles and has no `IProfileService`; the module-side `Role` lives only in the Users database and reaches services through `GetUserPermissionsRequest`. The project plan's "RBAC enforced at the API Gateway level (JWT role claim check)" **is not implementable today**. See Milestone I for what it would take, and §1/§9 for how defence-in-depth is achieved without it.

---

## 1. Architecture overview

| Module | Responsibility this feature |
|---|---|
| **Support** (`fooddeliveryservice_support`) — **new** | Owns `Ticket` (+ `TicketMessage`), `RefundRequest`, `SupportAuditEntry`. Keeps `Customer`, `SupportAgentReplica`, `OrderSnapshot` + `OrderTimelineEntry` replicas. Publishes `SupportTicketOpened`, `SupportTicketResolved`, `TicketMessagePosted`, `RefundRequested`, `RefundApproved`, `RefundRejected`. |
| **Users** (`fooddeliveryservice_users`) | Adds the support permission set and seeds it to `SupportAgent`, `Administrator` and (the two customer-facing codes) `Customer`. Nothing else — the role and the provisioning flow already exist. |
| **Notifications** (`fooddeliveryservice_notifications`) | Two new notification types + templates: "an agent replied to your ticket", "your refund request was approved/declined". Pure consumer, as today. |
| **RealTime** (`fooddeliveryservice_realtime`) | Milestone H only: a `SupportTicketFrame` to the existing global `support` group, and a `TicketMessageFrame` to the customer's own `user:{id}` group. Additive to `TrackingHubMethods`. |
| **Orders / Restaurants / Delivery** | **No work.** Support consumes the events they already publish. |

**End-to-end flow (ticket opened → resolved)**

1. A **customer** `POST support/tickets` with a subject, category and optional `orderId`. The ticket opens in `Open`, unassigned. `SupportTicketOpenedIntegrationEvent` is published; Milestone H turns it into a live row on every agent's dashboard.
2. An **agent** lists the queue (`GET support/tickets?status=Open&unassigned=true`) and **claims** one (`POST support/tickets/{id}/claim`) — guarded by the distributed lock so two agents cannot both take it.
3. The agent opens the ticket and reads its **context** (`GET support/tickets/{id}/context`): the customer's recent orders and the full timeline of the ticket's order, both served from Support's own replica.
4. The agent **replies** (`POST support/tickets/{id}/messages`) — visible to the customer, or an `InternalNote` that is filtered out of every customer-facing read. A customer-visible agent message publishes `TicketMessagePostedIntegrationEvent`; **Notifications** emails the customer and Milestone H pushes it to their SignalR group.
5. If the resolution is a refund, the agent **requests** one (`POST support/tickets/{id}/refund-requests`) — validated against the replicated order subtotal. An **administrator** approves or rejects it (`POST support/refund-requests/{id}/approve|reject`), which the requesting agent cannot do themselves. Either decision emails the customer. **No payment is processed and Orders consumes nothing.**
6. The agent moves the ticket through `InProgress` → `Resolved`, or `Escalated` when it needs a supervisor.
7. **Every** one of those agent actions — status change, assignment, refund decision — appends a `SupportAuditEntry` row **in the same transaction** carrying the agent id, UTC timestamp, before/after values and the supplied reason.
8. `GET support/analytics/summary` reports average resolution time, tickets per day, and the breakdown by category and status, over a cached Dapper read.

No new synchronous cross-service call is introduced. Support is a consumer of three modules' events and a publisher to two.

---

## 2. Milestone A — Users: the support permission set

**PR size: small.** One constants file, one seeding file, one migration, two test files.

### 2.1 Permissions (`Users.Domain/Users/Permission.cs`)
```csharp
// Support & ticketing (Phase 3, Feature 3.6). Namespaced `support-*` so they can never be
// confused with anything else; the platform has no other notion of a "ticket".
public static readonly Permission OpenSupportTicket    = new("support-tickets:open");    // customer: open a ticket, reply on their own
public static readonly Permission GetSupportTickets    = new("support-tickets:read");    // agent: read any; customer: read their own (ownership-scoped in the handler)
public static readonly Permission ManageSupportTickets = new("support-tickets:manage");  // agent: status transitions, internal notes, audit log
public static readonly Permission AssignSupportTickets = new("support-tickets:assign");  // agent: claim; admin: assign to anyone
public static readonly Permission RequestRefund        = new("refunds:request");         // agent
public static readonly Permission ApproveRefund        = new("refunds:approve");         // admin only — segregation of duties
public static readonly Permission GetSupportAnalytics  = new("support-analytics:read");
```

*Added when C was built (2026-08-28):* one more code, `support-tickets:administer`, granted to **Administrator only** — migration `Add_Support_Ticket_Administer_Permission`. §4.5 requires the caller to be an administrator to assign a ticket to a *different* agent, and no code in the set above expresses that: agents and administrators both hold `support-tickets:assign`. The only other admin-only support code is `refunds:approve`, and inferring "is an administrator" from it would silently hand out ticket routing the day a senior agent is granted refund approval — a privilege leaking through an unrelated grant, which is the trap the `support-*` namespace was carved out to avoid. This mirrors `deliveries:administer` exactly, which is the ownership-bypass shape §4.5 was already pointing at.

### 2.2 Seeding (`Users.Infrastructure/Users/PermissionConfiguration.cs`)
- **SupportAgent**: `support-tickets:read`, `:manage`, `:assign`, `refunds:request`, `support-analytics:read` — on top of the `support:dashboard`, `users:read`, `users:update` it already holds. This ends the *"No operational permissions — support is read-only"* comment currently on that block; update it rather than leaving it lying.
- **Administrator**: all of the above **plus** `refunds:approve` and `support-tickets:open`.
- **Customer**: `support-tickets:open` and `support-tickets:read` only. Ownership is enforced in the handlers — a customer reading a ticket that is not theirs gets a `404`, not a `403` (do not leak existence).
- **RestaurantManager / DeliveryDriver**: nothing. Partner-facing support is not in this feature.

Migration: `Add_Support_Role_Permissions`.

### 2.3 Tests
- *Unit* (`Users.UnitTests`): guard tests that codes stay unique and that no code revives a removed event-ticketing namespace (`events:`, `ticket-types:`, `categories:`, bare `tickets:`, `event-statistics:`) — cheap, and it catches the exact copy-paste regression this naming decision exists to prevent.
- *Integration* (`Users.IntegrationTests`): provision a `SupportAgent`, activate the invitation, assert `GetUserPermissionsRequest` returns the five agent permissions and **not** `refunds:approve`; assert a `Customer` gets `support-tickets:open`/`:read` and none of the others.

---

## 3. Milestone B — Support service skeleton + `Ticket` aggregate + CRUD

**PR size: large, but one coherent unit** — a new service that does exactly one thing end to end. Same shape as `DELIVERY_PHASE2_PLAN.md` §3, and it should be reviewed the same way: skeleton first, then the aggregate, then the four endpoints.

### 3.1 Projects & host
Five module projects (`Domain`, `Application`, `Infrastructure`, `Presentation`, `IntegrationEvents`) under `src/Modules/Support/`, plus `src/API/FoodDeliveryService.Support.Api` — copy the **Delivery** host bootstrap (`Program.cs`, `OpenTelemetry/DiagnosticsConfig.cs` with `ServiceName = "FoodDeliveryService.Support"`, `appsettings*.json`, `Dockerfile`). Add the five module projects, the host, and the two test projects to `FoodDeliveryService.Api.slnx`.

`SupportModule.cs` mirrors `DeliveryModule.cs`: `AddDomainEventHandlers`, `AddIntegrationEventHandlers`, `AddEndpoints`, `AddDbContext<SupportDbContext>` (Npgsql + `UseSnakeCaseNamingConvention` + `InsertOutboxMessagesInterceptor`), `IUnitOfWork`, repositories, outbox/inbox Quartz options, the MassTransit-RPC `IPermissionService`, and `ISupportContext` (the caller's id — for ownership checks and for the audit log).

### 3.2 The initial migration must be born current
`Create_Database` creates `outbox_messages` / `inbox_messages` **with** the `correlation_id` and `trace_parent` columns and **with** the message-dispatch index, in one migration. Do not replay the three historical migrations every other module accumulated (`Create_Database` → `Add_Message_Correlation_Columns` → `Add_Message_Dispatch_Index`); copy the *current* shape from `DeliveryDbContextModelSnapshot`. A missing dispatch index is the outbox regression `LOADTESTING_PHASE3_PLAN.md` Milestone F found the hard way.

In practice this needs no hand-authoring at all: a fresh `dotnet ef migrations add Create_Database` against the *current* `OutboxMessageConfiguration`/`InboxMessageConfiguration` already emits the two correlation columns and both partial `ix_*_unprocessed` indexes, because those configurations live in `Common.Infrastructure` and are already current. What the generated file does need is two edits before it compiles under `TreatWarningsAsErrors` + `AnalysisMode=All`: convert it to a file-scoped namespace (IDE0161) and lift its `columns: new[] { ... }` composite-index argument to a `private static readonly string[]` field (CA1861). Every existing module's migrations carry the same two edits — the `.Designer.cs` and snapshot files are exempt because they are marked `// <auto-generated />`.

### 3.3 `Ticket` aggregate (`Domain/Tickets/Ticket.cs`)
```csharp
public sealed class Ticket : Entity
{
    public Guid Id { get; private set; }
    public string Reference { get; private set; }             // human-quotable, e.g. "SUP-00001234"
    public Guid CustomerId { get; private set; }
    public Guid? OrderId { get; private set; }                // nullable: not every ticket is about an order
    public string Subject { get; private set; }
    public TicketCategory Category { get; private set; }      // OrderNotReceived, ItemMissing, FoodQuality, DriverIssue, PaymentIssue, AppIssue, Other
    public TicketPriority Priority { get; private set; }      // Low, Normal, High, Urgent
    public TicketStatus Status { get; private set; }          // Open, InProgress, Resolved, Escalated, Closed
    public TicketSource Source { get; private set; }          // CustomerPortal, AgentCreated, Chatbot, FraudFlag
    public string? EscalationTranscript { get; private set; } // jsonb; reserved for Feature 3.1. Nothing writes it here.
    public Guid? AssignedAgentId { get; private set; }
    public DateTime OpenedOnUtc { get; private set; }
    public DateTime? FirstRespondedOnUtc { get; private set; }
    public DateTime? ResolvedOnUtc { get; private set; }
    public DateTime? ClosedOnUtc { get; private set; }
}
```

Standard shape: private ctor, `private set` throughout, static `Create`, guarded transition methods returning `Result` and raising a domain event each. `TicketErrors` holds every `Error`.

**Transitions** — the state machine *is* the domain logic, and it is where the unit tests live:

| Method | Legal from | Rules |
|---|---|---|
| `Create(...)` | — | Subject non-empty, ≤ 200 chars. Opens `Open`, unassigned, `Priority.Normal` — except category `OrderNotReceived`, which opens `High`. Raises `TicketOpenedDomainEvent`. |
| `StartProgress(agentId, utcNow)` | `Open`, `Escalated` | Requires an assigned agent. |
| `Resolve(agentId, resolution, utcNow)` | `InProgress`, `Escalated` | Resolution note required. Stamps `ResolvedOnUtc` — the numerator of average resolution time. |
| `Escalate(agentId, reason, utcNow)` | `Open`, `InProgress` | Reason required. Keeps the current assignee. |
| `Reopen(actorId, utcNow)` | `Resolved` | Only within 7 days of `ResolvedOnUtc`; clears `ResolvedOnUtc`, returns to `InProgress`. |
| `Close(actorId, utcNow)` | `Resolved` | Terminal. Nothing transitions out of `Closed`. |

Every illegal transition returns `Result.Failure(TicketErrors.InvalidTransition(from, to))` — **never throws**. A no-op (resolving an already-`Resolved` ticket) returns failure and raises **no** domain event; test that explicitly.

The signatures above drop the `utcNow` parameter wherever the method does not stamp a timestamp — `StartProgress(agentId)` and `Escalate(agentId, reason)`. An unused parameter is a build error under this repo's `TreatWarningsAsErrors` + `AnalysisMode=All`. The same applies to all three §4.2 signatures — none of them stamps a timestamp either, so they were built as `Claim(agentId)`, `AssignTo(agentId, actorId)` and `Unassign(actorId, reason)`. Every domain event carries `OccurredOnUtc` from the `DomainEvent` base, so nothing is lost.

**The assignment seam — resolved when B was built (2026-08-19).** Four of these six transitions require an assigned agent, and nothing in this milestone can assign one: `Claim`/`AssignTo`/`Unassign` are §4.2. Taken literally, B would ship `StartProgress`, `Resolve`, `Reopen` and `Close` as unreachable *and* untestable code, and §3.7's "every legal move" would be unwritable. The fix is a single internal write path on the aggregate:

```csharp
// Ticket.cs — the ONLY place AssignedAgentId is written.
internal void SetAssignedAgent(Guid? agentId) => AssignedAgentId = agentId;
```

`internal`, with `InternalsVisibleTo` granted to `Support.UnitTests` only. No command handler can reach it, so B still ships **no** way for an agent to take a ticket — which is the point, because an unguarded second assignment path is exactly the race §4.3's lock exists to prevent. The unit suite builds `InProgress`/`Resolved`/`Closed` tickets through it and covers the whole table.

`Escalate` from `Open` is therefore the only agent transition reachable end to end in B, since it is the one whose meaning does not depend on somebody owning the ticket. That is what §3.7's integration suite asserts for the success case; every other status call in B is a legitimate `NotAssigned` or `InvalidTransition` failure.

`Reference` comes from a Postgres sequence (`support_ticket_reference_seq`) read in the repository — not `MAX()+1`, which is a race the moment there are two replicas.

### 3.4 Endpoints (`Presentation/Tickets/`)
| Endpoint | Permission | Notes |
|---|---|---|
| `POST support/tickets` | `support-tickets:open` | `CustomerId` comes from `ISupportContext`, **never** the body. An agent-created ticket (`Source.AgentCreated`) may name a customer — that variant is gated on `support-tickets:manage`. |
| `GET support/tickets` | `support-tickets:read` | Paged + filtered (`status`, `category`, `assignedAgentId`, `unassigned`, `from`/`to`). An agent sees all; a customer's query is silently narrowed to their own `CustomerId` in the handler. Dapper. |
| `GET support/tickets/{id}` | `support-tickets:read` | Ownership-scoped: another customer's ticket is `404`. Dapper. |
| `POST support/tickets/{id}/status` | `support-tickets:manage` | Body `{ status, reason }`, dispatched to the matching aggregate method. One endpoint rather than five verb endpoints, because the aggregate already owns the legality table. |

Response DTOs only, never the entity (hard rule #3). Reads are Dapper via `IDbConnectionFactory` (hard rule #2); writes are EF + `IUnitOfWork.SaveChangesAsync()` (hard rule #6).

### 3.5 Integration events published (`Support.IntegrationEvents`)
`SupportTicketOpenedIntegrationEvent`, `SupportTicketResolvedIntegrationEvent` — full snapshots (hard rule #9): reference, customer id, order id, category, priority, status, agent id, timestamps. Nothing consumes them yet; Notifications and Milestone H do.

### 3.6 New-service cross-cutting checklist
This is the part that gets forgotten. All of it belongs in this PR:

- [ ] `docker-compose.yml`: `fooddeliveryservice.support.api`, ports `5700:8080` / `5701:8081`, `depends_on` database/redis/queue, `OTEL_EXPORTER_OTLP_ENDPOINT` → `http://fooddeliveryservice.otel-collector:4317`.
- [ ] Gateway `appsettings.Development.json`: route `fooddeliveryservice-support-route1` → `support/{**catch-all}`, `"AuthorizationPolicy": "default"`, and cluster `fooddeliveryservice-support-cluster` → `http://fooddeliveryservice.support.api:8080`. **Check `appsettings.json` too — its `Routes` is `{}` today, and a route missing from the environment actually deployed is the exact undefined-cluster bug `KUBERNETES_PHASE2_PLAN.md` found on `users/register`.**
  - *Resolved when B was built:* the deployed environment is not `appsettings.json` — the k8s ConfigMap in `deploy/k8s/services/gateway.yaml` is mounted as `appsettings.Kubernetes.json` and carries the whole routing table, precisely because the base file is empty. So the two places to add a route are **`appsettings.Development.json` and that ConfigMap**, which is what `gateway.yaml`'s own header says. `appsettings.json` was left empty: a lone `support` entry there, with the other seven services still absent, would be a worse trap than an empty section. Filling it in for all of them is its own change.
- [ ] Rate limiting: **no change required.** `RateLimitRoutePolicy.Classify` falls back to `Read` for GET/HEAD/OPTIONS and `Write` for everything else, which is right for every route here — support has no lifecycle transition a `429` would strand. Do not add a `Critical` line.
- [ ] `deploy/k8s/services/support.yaml` + the `support` keys in `deploy/k8s/base/config.yaml`; confirm `policy-check.py` and `cluster-smoke.sh` both pick it up.
- [ ] Prometheus scrape target + blackbox probe on `/health/live` and `/health/ready` (`docker/prometheus`, `docker/blackbox`).
- [ ] `.github/workflows/ci.yml`: add `Support.UnitTests` and `Support.IntegrationTests` to the **hardcoded** suite list — a test project that is not listed simply never runs.
- [ ] Host wiring: `AddHostTelemetry` (via `AddInfrastructure`), `AddModuleDiagnostics(SupportDiagnostics.Name)`, `app.UseRequestCorrelation()` before `UseSerilogRequestLogging()`, `app.MapHealthProbes()`, `app.ApplyMigrations()`.

**What `SupportDiagnostics` carries in B, and why it is only two instruments.** `SupportDiagnostics` lives in `Support.Application/Diagnostics/` over the shared `AppDiagnostics` (the Application layer, because the domain-event handlers that record business measurements cannot reference `Common.Infrastructure`). B declares `support.tickets.opened` — a counter tagged by category, which is what reads the queue as a product-quality signal rather than a staffing one — and `support.tickets.resolution.duration`, a histogram computed from the two timestamps the resolved event already carries, so a message dispatched minutes late still reports the duration the customer experienced. Both are recorded **last** in their handler, after the publish, because `IdempotentDomainEventHandler` only writes its consumer row once `Handle` returns and a handler that throws is re-run whole.

A lifecycle *transition* counter is deliberately **not** here, even though Orders has one. Most of this state machine cannot be driven until §4.2 ships assignment, so the series would graph as a permanently flat line — which reads as "nothing is happening" rather than as "this is not built yet".

*Resolved when C was built:* C makes those transitions reachable but still does **not** add the counter — it lands in §8.2 with the rest of the instrument set. `ObservabilityAssetTests` fails the build if a dashboard names a metric nothing emits, so the instrument and its Grafana panel want to be in one PR, and G is where that panel is. C's endpoints are covered meanwhile by `RequestMetricsBehavior`, which measures every command without any handler recording anything.

### 3.7 Tests
- *Unit* (`Support.UnitTests`, new project): the full transition table above — every legal move, every illegal move returning the right `Error`, the no-op-raises-no-event cases, `Reopen` outside the 7-day window, `Create` validation, and the `OrderNotReceived → High` priority rule. The assignment-dependent half is reachable through the `SetAssignedAgent` seam (§3.3); add the boundary cases the table implies but does not spell out — `Reopen` on the *last* day of the window, a subject of exactly 200 characters, and one test that walks every transition against a `Closed` ticket to prove the terminal state is terminal. **35 tests as built.**
- *Integration* (`Support.IntegrationTests`, new project, Testcontainers + real Duende JWTs): a customer opens a ticket and reads it back; a second customer gets `404` on it; an agent lists and sees both; a customer without `support-tickets:manage` gets `403` on the status endpoint; an illegal transition returns `400` with problem details; `SupportTicketOpenedIntegrationEvent` lands in `outbox_messages`.
  Three more the list implies and B added: the `?status=Open&unassigned=true` queue filter actually narrows; a customer's own list excludes the other customer's ticket (the read-scoping in the `WHERE` clause, not just the single-ticket `404`); and `OnBehalfOfCustomerId` from a customer is refused, since that is the one field in any request body that can name somebody else. The success case for the status endpoint is `Escalated`, for the reason in §3.3. **10 tests as built.**
  The outbox assertion reads `outbox_messages` for `TicketOpenedDomainEvent` — the *domain* event, which is what the interceptor writes in the ticket's transaction; the integration event is what `ProcessOutboxJob` publishes from it, and asserting on the row is what proves the transactional write rather than the dispatch.
  Note the suite seeds three users (agent, customer, second customer) and needs `fooddeliveryservice.identity` up on `:18080`, so like the other Identity-dependent suites it is **not** in `ci.yml`'s hardcoded list — only `Support.UnitTests` is.

---

## 4. Milestone C — Assignment + the audit log

**PR size: medium.** The two halves belong in one PR: assignment is the first agent action, and the audit log is what makes agent actions accountable — shipping either alone leaves a gap the other closes.

### 4.1 `SupportAgentReplica` (`Domain/Agents/`)
Consume `UserRegisteredIntegrationEvent` / `UserProfileUpdatedIntegrationEvent` in `Support.Presentation/Agents/`, keeping a row **only** where the role is `SupportAgent` or `Administrator`. Id, first/last name, email, active flag. This is what lets a ticket list render "assigned to Jane Doe" without a cross-service call, and what validates that an assignment target exists.

Built as `support_agents` (the `Replica` suffix describes how the row arrived, not what the table holds), fed by `UpsertSupportAgentCommand` / `UpdateSupportAgentCommand` — the Orders `Customer` shape, with the update no-opping for the overwhelming majority of users who are not staff. The two consumers are registered in `SupportModule.ConfigureConsumers`, which is what turned its discarded `instanceId` parameter into a used one.

`Support.Presentation` gained a project reference to `Users.IntegrationEvents` for them — the first one it has; Infrastructure already had it for the permissions RPC.

### 4.2 Assignment on the aggregate
```csharp
public Result Claim(Guid agentId, DateTime utcNow);                  // Open/Escalated + unassigned only
public Result AssignTo(Guid agentId, Guid actorId, DateTime utcNow); // admin override; may reassign an assigned ticket
public Result Unassign(Guid actorId, string reason, DateTime utcNow);
```
Each raises its domain event. `Claim` on an already-assigned ticket returns `TicketErrors.AlreadyAssigned` — the aggregate guard, which the lock complements and never replaces.

**All three write the assignee through the `internal SetAssignedAgent` seam Milestone B added (§3.3)** — they are the public, guarded wrappers it was built for. Do not assign `AssignedAgentId` directly here: a second write path to that field is precisely the unguarded race §4.3 exists to close, and keeping one setter is what makes "every assignment went through a guard" a property of the code rather than a convention. When these land, the seam's `InternalsVisibleTo` comment should stop calling the unit tests its only consumer.

### 4.3 The distributed lock on `Claim`
`Claim` is check-then-act on state another caller can change, no aggregate carries a concurrency token, and losing the race double-books a scarce resource (two agents writing the same reply). Take `IDistributedLock`:

```csharp
// Acquired BEFORE the read — the check-then-act begins at the read, so a lock taken
// after it still lets both agents act on the same stale snapshot.
await using IAsyncDisposable? handle =
    await distributedLock.TryAcquireAsync(SupportLocks.Ticket(ticketId), SupportLocks.ClaimTtl, ct);
if (handle is null) return Result.Failure(TicketErrors.ClaimInProgress);
```

Keys and TTL live in **one** shared static — `Application/Abstractions/Locking/SupportLocks.cs` — so the read and write sides cannot drift onto different names. TTL 5 s: comfortably longer than the critical section, far shorter than any business window. A lost acquisition returns a failure the agent's UI retries; it strands nothing, because the ticket is still sitting in the queue.

**All three assignment paths take it, under the same key** — not just `Claim`. `AssignTo` and `Unassign` write the very field `Claim` races over, so leaving either outside would reopen the race from a second door, and a *different* key per operation would fail to serialize an admin assignment against an agent's simultaneous claim. One key, `SupportLocks.Ticket(ticketId)`, one TTL.

The §4.6 concurrency test asserts on `Tickets.ClaimInProgress` **or** `Tickets.AlreadyAssigned`, not on one of them: the loser hits the lock if it arrives while the winner holds it and the aggregate guard if it arrives after the winner commits. Both are correct, and pinning the test to one makes it a test of timing.

### 4.4 `SupportAuditEntry` (`Domain/Audit/`)
Append-only. No update path, no delete path, no domain events (it *is* the record).

```csharp
public Guid Id { get; }
public Guid TicketId { get; }
public Guid ActorId { get; }              // the agent or admin — from ISupportContext, never the body
public SupportAuditAction Action { get; } // StatusChanged, Assigned, Unassigned, Claimed, MessagePosted, RefundRequested, RefundApproved, RefundRejected
public string? FromValue { get; }
public string? ToValue { get; }
public string? Reason { get; }
public DateTime OccurredOnUtc { get; }
```

**Written in the same `SaveChangesAsync` as the state change it records.** Not in a domain-event handler — the outbox lag would let a transition commit while its audit row fails independently, which is precisely the accountability hole the log exists to close. A small `ISupportAuditWriter` in the Application layer, called by each command handler immediately before `SaveChangesAsync`, keeps that from being re-derived per handler.

*As built:* the **interface** is in `Application/Abstractions/Audit/`, the implementation in `Infrastructure/Audit/SupportAuditWriter.cs` — an Application-layer implementation cannot be `internal` and still be registered from `SupportModule`, and every other abstraction here already splits that way (`ISupportContext` → `Infrastructure/Authentication/SupportContext`). It is synchronous and `void`: it only stages the entity, and the `SaveChangesAsync` the handler was already going to call is what commits it, so there is no second transaction to get wrong. `ActorId` and `OccurredOnUtc` are deliberately not parameters — resolving them inside from `ISupportContext` and `IDateTimeProvider` is what makes them unforgeable by a request body.

`support_audit_entries` carries **no foreign key to `tickets`**. A cascade is the one thing that could ever delete an audit row, and an append-only log must have no delete path at all, transitive included; the audit read checks the ticket exists with its own statement instead — an untouched ticket has an empty history, which is a different answer from a ticket that does not exist.

`FromValue`/`ToValue`/`Reason` are truncated in `SupportAuditEntry.Create` rather than rejected: a failed audit write would roll back the state change it was recording, turning a cosmetic over-length problem into a refused agent action.

Retrofit the Milestone B status endpoint to write one too.

### 4.5 Endpoints
| Endpoint | Permission |
|---|---|
| `POST support/tickets/{id}/claim` | `support-tickets:assign` |
| `POST support/tickets/{id}/assign` | `support-tickets:assign`, **and** the caller must be an Administrator to name a different agent — enforced in the handler against the new admin-only `support-tickets:administer` (see §2.1), because the route policy cannot see the body. An agent naming *themselves* is allowed: it is the assign-side equivalent of a claim, and unlike `Claim` it can take over an `InProgress` ticket |
| `POST support/tickets/{id}/unassign` | `support-tickets:assign` |
| `GET support/tickets/{id}/audit` | `support-tickets:manage` — agents and admins only; **never** exposed to the customer, since internal reasons appear here. Dapper, newest first. |

### 4.6 Tests
- *Unit* (`TicketAssignmentTests`, its own file — assignment is not part of the status machine): `Claim` on unassigned succeeds and raises the event; on assigned returns `AlreadyAssigned` and raises nothing; the three non-queue statuses each return `NotClaimable`; `AssignTo` reassigns and carries the outgoing agent on the event; re-assigning to the current assignee is a no-op failure that raises nothing; `Unassign` requires a reason and leaves the ticket claimable again. **31 tests as built (54 in the project).**
- *Integration*: two concurrent `claim` calls for one ticket → exactly one `204`, one clean failure, and exactly **one** `Claimed` audit row (assert on the audit table, not just on the responses — that is what actually proves the lock); the audit endpoint returns entries for a status change, newest first; a customer gets `403` on the audit endpoint **even for their own ticket** (a `403` and not a `404` here on purpose — the customer already knows that ticket exists); an agent replica row appears after a `UserRegisteredIntegrationEvent` with role `SupportAgent` and does **not** for a `Customer`; an admin reassignment records both halves; assigning to a non-agent id is a `404`. **14 tests as built (24 in the project).**

**Two harness gotchas this milestone cost real time on, and the next replica milestone (§5) will hit both.**

1. **Both hosts must be built before the first user is seeded.** Seeding raises `UserRegisteredDomainEvent`, the Users outbox publishes from it within a second, and MassTransit publishes to an *exchange* — a message with no queue bound to it is **dropped, not queued**. Seed before Support's consumers exist and the replica is simply never built, with no error anywhere. `IntegrationTestWebAppFactory.InitializeAsync` now touches `_usersApiFactory.Services` and `Services` before seeding, in that order.
2. **`UsersApiTestFactory` needs the 1-second outbox/inbox intervals too.** It is built first — during seeding — so the env vars `IntegrationTestWebAppFactory.ConfigureWebHost` sets have not been applied to it yet, and at the production interval every replica assertion races the projection.

Also: `Role.Administrator` is absent from `Role.Assignable`, so nobody can be *provisioned* as one — but `User.Create` takes a `Role` directly, which is how the fixture seeds the administrator the bypass test needs. And the CA2025 analyzer rejects an `HttpClient` in a `using` scope being handed to a task that is not awaited in the same statement, so the concurrency test awaits `Task.WhenAll(...)` on the call expressions directly rather than storing the two tasks first.

---

## 5. Milestone D — Order & customer context replicas

**PR size: medium.** Almost no write-side surface — this is projection work plus one read endpoint.

### 5.1 `Customer` replica
Same pattern as `Orders.Domain/Customers/Customer.cs`: upsert from `UserRegisteredIntegrationEvent`, update from `UserProfileUpdatedIntegrationEvent`, handlers in `Support.Presentation/Customers/`. Id, name, email — so the ticket list shows who is asking.

### 5.2 `OrderSnapshot` + `OrderTimelineEntry`
One `IIntegrationEventHandler<T>` per event in `Support.Presentation/Orders/`, registered in `SupportModule.ConfigureConsumers` with `.Endpoint(c => c.InstanceId = instanceId)`:

| Event | Effect on `OrderSnapshot` | Timeline entry |
|---|---|---|
| `OrderPlacedIntegrationEvent` | insert (customer, restaurant, subtotal, placed-on) | `Placed` |
| `OrderAcceptedIntegrationEvent` | status → `Accepted` | `Accepted` |
| `OrderRejectedIntegrationEvent` | status → `Rejected`, store reason | `Rejected` |
| `OrderReadyForPickupIntegrationEvent` | status → `ReadyForPickup`, store the delivery address | `ReadyForPickup` |
| `OrderCancelledIntegrationEvent` | status → `Cancelled` | `Cancelled` |
| `DriverAssignedIntegrationEvent` | store driver id + name + vehicle | `DriverAssigned` |
| `OrderPickedUpIntegrationEvent` | status → `OutForDelivery` | `PickedUp` |
| `OrderDeliveredIntegrationEvent` | status → `Delivered` | `Delivered` |

Two properties of the projection matter enough to test:

- **Idempotent.** `IdempotentIntegrationEventHandler` already dedupes on message id, but the *snapshot* must tolerate replay independently — an upsert, not an insert. The timeline is keyed on `(OrderId, Kind, OccurredOnUtc)` so a redelivery cannot duplicate a row.
- **Out-of-order tolerant.** Nothing guarantees `OrderAccepted` is consumed before `DriverAssigned`. Insert the snapshot on *any* event naming an unknown `OrderId` (a partial row beats a dropped one), and never let a later-arriving earlier event overwrite a more advanced status — compare `OccurredOnUtc`, do not blindly assign. This is a real hazard the moment the service runs more than one replica.

Put the derivation in a code comment: Orders publishes no `OutForDelivery`/`Delivered` integration event of its own, so those two snapshot states come from **Delivery's** events. Someone will otherwise go looking for them in `Orders.IntegrationEvents`.

### 5.3 `GET support/tickets/{id}/context`
Permission `support-tickets:read`, ownership-scoped like the ticket read. Returns, one Dapper round trip per section:
- the ticket's order snapshot + its full timeline (when `OrderId` is set),
- the customer's last 10 orders with status and subtotal,
- counts that give an agent instant judgement: total orders, cancelled orders, prior tickets, prior refunds.

Those counts are also exactly the "many orders cancelled before pickup" and "high complaint rate" signals Feature 3.4 would have computed. They are presented here as **context for a human**, not as a score — the honest version of that idea while no fraud service exists.

### 5.4 Tests
- *Integration* (where this milestone's value is proven): publish the eight events in order → the snapshot ends `Delivered` with a complete eight-entry timeline; publish them **out of order** (`OrderDelivered` before `OrderAccepted`) → the snapshot still ends `Delivered`, not regressed to `Accepted`; publish one twice → no duplicate timeline row; `GET .../context` returns the timeline for a ticket's order; a customer gets `404` for another customer's ticket context.

---

## 6. Milestone E — Ticket messaging + customer notification

**PR size: medium.** Touches Support and Notifications.

### 6.1 `TicketMessage` (child of the `Ticket` aggregate)
Not a separate aggregate — a message is meaningless without its ticket, and posting one is a state change *on* the ticket (it can stamp `FirstRespondedOnUtc`). Added through `Ticket.PostMessage(...)`, which owns the rules:

```csharp
public Result<TicketMessage> PostMessage(
    Guid authorId, TicketAuthorKind kind, string body, TicketMessageVisibility visibility, DateTime utcNow);
```

- Body non-empty, ≤ 4000 chars.
- **A `Closed` ticket accepts no messages.** A `Resolved` one does — that is how a customer reopens a conversation — and it moves back to `InProgress`.
- **A customer may only post `CustomerVisible`.** Enforced in the aggregate, not only at the endpoint: an `InternalNote` authored by a customer is a data-integrity bug, not an authorization one.
- The first `CustomerVisible` message from an agent stamps `FirstRespondedOnUtc`; a second one does not move it.
- Raises `TicketMessagePostedDomainEvent`.

`TicketMessage`: id, ticket id, author id, author kind (`Customer`/`Agent`/`System`), body, visibility, `PostedOnUtc`.

### 6.2 Endpoints
| Endpoint | Permission | Notes |
|---|---|---|
| `POST support/tickets/{id}/messages` | `support-tickets:open` (customer, own ticket) or `support-tickets:manage` (agent) | Visibility defaults to `CustomerVisible`; `InternalNote` requires `support-tickets:manage`. |
| `GET support/tickets/{id}/messages` | `support-tickets:read` | **Internal notes are excluded in the SQL for a customer caller — not in the DTO mapper.** A projection that fetches them and drops them later is one refactor away from leaking them. |

Both write a `MessagePosted` audit entry.

### 6.3 Notifications
`TicketMessagePostedIntegrationEvent` is published **only** for `CustomerVisible` messages authored by an agent — a customer does not get emailed about their own message, and an internal note must never leave the building. It carries the ticket reference, customer id, subject and a **truncated** body preview.

In Notifications: a new `NotificationType.SupportTicketReply`, a template, and a `TicketMessagePostedIntegrationEventHandler` in `Notifications.Presentation/Support/` that sends through the existing `SendNotificationCommand` path. The `RecipientUser` replica already holds the email address — no new replica needed.

### 6.4 Tests
- *Unit*: a customer cannot post an `InternalNote`; a `Closed` ticket rejects a message; posting on a `Resolved` ticket moves it to `InProgress`; the first agent reply stamps `FirstRespondedOnUtc` and the second does not.
- *Integration*: an agent's internal note is absent from the customer's `GET .../messages` and present in the agent's; a customer-visible agent reply produces a `Notification` row of type `SupportTicketReply` in the Notifications database (host Notifications in-process, as the existing cross-module tests do); an internal note produces **no** notification.

---

## 7. Milestone F — Refund request workflow

**PR size: small-to-medium.** Self-contained; depends on D for the order subtotal.

### 7.1 `RefundRequest` aggregate (`Domain/Refunds/`)
```csharp
public Guid Id { get; }
public Guid TicketId { get; }
public Guid OrderId { get; }
public Guid CustomerId { get; }
public decimal Amount { get; }
public string Reason { get; }
public RefundStatus Status { get; }     // Requested, Approved, Rejected
public Guid RequestedByAgentId { get; }
public Guid? DecidedByAdminId { get; }
public string? DecisionNote { get; }
public DateTime RequestedOnUtc { get; }
public DateTime? DecidedOnUtc { get; }
```

- `Create`: amount > 0 and ≤ the order subtotal **from the replicated `OrderSnapshot`** (the handler reads it and passes it in — the aggregate does not reach for data); reason required; the ticket must name that order.
- `Approve(adminId, note, utcNow)` / `Reject(adminId, note, utcNow)`: legal from `Requested` only; a second decision returns `RefundErrors.AlreadyDecided`.
- **`RequestedByAgentId != adminId`** — enforced in the aggregate. Segregation of duties is a domain invariant here, not a policy checkbox, and stating it that way is the point of the feature.
- At most one non-rejected request per order — a unique partial index plus an aggregate check, since two agents on two tickets for the same order is a plausible race.

### 7.2 The lock on the decision
`Approve`/`Reject` is check-then-act on `Status` with no concurrency token, and a double approval is the one outcome here with real-world consequences. Same `IDistributedLock` shape as §4.3, key `SupportLocks.Refund(refundRequestId)`.

### 7.3 Endpoints
| Endpoint | Permission |
|---|---|
| `POST support/tickets/{id}/refund-requests` | `refunds:request` |
| `GET support/refund-requests` | `refunds:request` — paged, filter by status; the admin's approval queue |
| `POST support/refund-requests/{id}/approve` | `refunds:approve` |
| `POST support/refund-requests/{id}/reject` | `refunds:approve` |

All four write audit entries. Publishes `RefundRequestedIntegrationEvent`, `RefundApprovedIntegrationEvent`, `RefundRejectedIntegrationEvent`.

### 7.4 Notifications
`NotificationType.RefundDecision` + template; a handler on the approved/rejected events emails the customer. **Nothing in Orders consumes these** — say so in a comment on the events themselves, because the natural assumption on reading `RefundApproved` is that money moved. It did not: this platform has no payment processing by design, and the record exists so a real payment integration could be added behind it later.

### 7.5 Tests
- *Unit*: an amount over the subtotal fails; zero/negative fails; the requesting agent approving their own request fails; a second decision fails; approve from `Rejected` fails.
- *Integration*: agent requests → admin approves → a `RefundDecision` notification is sent and two audit rows exist; an agent without `refunds:approve` gets `403` on approve; two concurrent approvals yield exactly one `Approved` and one clean failure.

---

## 8. Milestone G — Support analytics summary + business metrics

**PR size: small.**

### 8.1 `GetSupportSummaryQuery : ICachedQuery<SupportSummaryResponse>`
Permission `support-analytics:read`. Parameters `from`/`to`, defaulting to the last 30 days. One Dapper handler, one statement per section:

- **Average *and median* resolution time** — `ResolvedOnUtc - OpenedOnUtc` over tickets resolved in the window. Report both, for the same reason `LOADTESTING_PHASE3_PLAN.md` reports p95 rather than a mean: one week-old ticket drags the average and hides the typical experience.
- **Average first-response time** — `FirstRespondedOnUtc - OpenedOnUtc`. The metric support teams are actually measured on.
- **Tickets per day** — a date-series count, gap-filled with zeroes via `generate_series` so a chart does not silently skip quiet days.
- **Breakdown by category, by status, and by assigned agent** (agent name joined from the replica).
- **Refund totals** — count and summed amount by status.

Caching: `ICachedQuery`, **5-minute TTL, no invalidation**. This is a deliberate departure from `CLAUDE.md`'s inline-`RemoveAsync` rule and the reason belongs in a comment: that rule governs entity-keyed reads whose staleness a user experiences as a bug. A 30-day aggregate is not entity-keyed — every ticket write would have to evict it — and a five-minute-old management summary is the norm. Key via a `SupportCacheKeys` convention class built on `CacheKeys.Create`, never concatenated at the call site.

### 8.2 Business metrics (`SupportDiagnostics`)
A `{Module}Diagnostics` static over `AppDiagnostics`, registered with `AddModuleDiagnostics(SupportDiagnostics.Name)` in the host — an unregistered meter records into nothing and never errors.

| Instrument | Tags | Recorded where |
|---|---|---|
| `support.tickets.opened` (counter) | `category`, `source` | beside the `TicketOpened` domain-event handler, **last**, so an outbox retry cannot double-count |
| `support.tickets.transition` (counter) | `from`, `to` | the status-change handler — reachable since §4.2 shipped assignment, and deliberately left here rather than added there, so the instrument and its Grafana panel land in one PR (§3.6) |
| `support.ticket.resolution_time` (histogram, seconds) | `category` | the resolve handler |
| `support.refunds.decided` (counter) | `outcome` | the refund decision handlers |

Enum values only — never a ticket id, agent id or free-text reason.

Add a Grafana panel in `docker/grafana`. `ObservabilityAssetTests` fails the build if a dashboard names a metric nothing emits, so the dashboard and the instruments must land in the same PR — and mind the Prometheus rename (`support.tickets.opened` → `support_tickets_opened_total`).

### 8.3 `docs/support-ticketing.md`
The reference document, matching `docs/caching.md` / `docs/rate-limiting.md`: the ticket state machine as a diagram, the permission matrix, what the audit log guarantees, the projection table from §5.2, why refunds move no money, and the analytics definitions. Link it from the root `README.md`.

### 8.4 Tests
- *Integration*: seed tickets with known timestamps → counts, mean and median match hand-computed values; a quiet day appears as a zero rather than a missing row; a second identical call inside the TTL is served from cache (assert a stable response across an intervening write, or on the cache-hit counter).

---

## 9. Milestone H *(optional, portfolio)* — Real-time support push

**PR size: small.** Purely additive to RealTime.

- `SupportTicketFrame(Guid TicketId, string Reference, string Status, string Category, Guid? AssignedAgentId, DateTime OccurredOnUtc)` → the existing global `support` group, on `SupportTicketOpened` / `SupportTicketResolved`. Agent queues update live without polling.
- `TicketMessageFrame(Guid TicketId, string Reference, string Preview, DateTime PostedOnUtc)` → the customer's own `user:{customerId}` group, on `TicketMessagePosted`. A new `IRealTimeNotifier.NotifyUserAsync` overload, same best-effort contract as the others (swallow-and-log; the client re-syncs from REST on reconnect).
- Two new constants on `TrackingHubMethods` (`SupportTicketActivity`, `TicketMessageReceived`) — additive only, as that file's contract requires.
- Two consumers in `RealTime.Infrastructure/Consumers/`, each with its own `InstanceId`.
- **No new group and no new permission**: the `support` group and `support:dashboard` already exist and are already joined from claims in `TrackingHub.OnConnectedAsync`.

*Tests*: extend `DashboardFanOutTests` — a support-group client receives the ticket frame and a non-support client does not; the ticket's customer receives the message frame and a different customer does not.

---

## 10. Milestone I *(optional, and larger than it sounds)* — Gateway role-claim RBAC

The project plan specifies RBAC "enforced at the API Gateway level (JWT role claim check) and repeated at the service level as a defence-in-depth measure". **Milestones A–H deliver the service level; the gateway level is not implementable as specified**, because no role claim exists in the token: Identity registers `IdentityRole` but assigns nothing, has no `IProfileService`, and the authoritative role lives in the Users module's database.

The defence-in-depth story A–H actually ships is still two-layered, and worth stating plainly rather than papering over: the **Gateway** validates the JWT and rejects unauthenticated traffic before any proxying (`"AuthorizationPolicy": "default"` on `support/**`), and each **service** re-validates the token independently and resolves fine-grained permissions through `GetUserPermissionsRequest`. What is missing is coarse role filtering at the edge — an optimisation, not the security boundary.

Closing it properly means:
1. Users→Identity role sync (Identity never learns the module-side role today), or a Duende `IProfileService` that calls Users — a new synchronous cross-service dependency in the token-issuance path, which needs its own justification.
2. A `role` claim in the access token, plus `AddPolicy("support", …)` in the Gateway wired to `"AuthorizationPolicy": "support"` on the route.
3. Re-issued tokens for every existing session.

**Recommendation: run it as its own feature, not inside 3.6.** It changes the token contract for every client, and it is already a listed prerequisite of the Angular frontend plan (which needs a roles claim to render role-specific navigation) — better scoped there, where both consumers are considered together.

---

## 11. Cross-cutting checklist

- [ ] No cross-service HTTP or database access — replicas only (hard rules #4, #5).
- [ ] Every read handler is Dapper via `IDbConnectionFactory`; every write goes through `IUnitOfWork.SaveChangesAsync()` (hard rules #2, #6).
- [ ] No domain entity crosses an endpoint boundary (hard rule #3).
- [ ] All business rules live on `Ticket` / `RefundRequest`, not in handlers (hard rule #1).
- [ ] Every integration event carries a full snapshot (hard rule #9).
- [ ] Actor ids come from `ISupportContext`, never a request body — an audit log an agent can forge is worse than none.
- [ ] Ownership failures return `404`, not `403`, for another customer's ticket.
- [ ] Support runs multiple replicas: no in-process `lock`, no reliance on `[DisallowConcurrentExecution]`, and the outbox/inbox `SKIP LOCKED` behaviour `KUBERNETES_PHASE2_PLAN.md` Milestone D covers applies from day one.
- [ ] Correlation columns and the dispatch index exist in the **initial** migration (§3.2).
- [ ] Every milestone's PR keeps `dotnet build` clean and the full suite green; the CI suite list is updated in Milestone B, not at the end.

---

## 12. Definition of done

1. An admin provisions a `SupportAgent`; the invitation activates and the agent obtains a JWT carrying the five support permissions.
2. A customer opens a ticket about an order and sees it in their own list; another customer cannot.
3. Two agents claim the same ticket concurrently — exactly one wins, and the audit log holds exactly one assignment entry.
4. The agent opens the ticket context and sees the order's complete lifecycle timeline, built entirely from replicated events.
5. The agent posts an internal note (invisible to the customer) and a reply (which reaches the customer by email, and by SignalR if H shipped).
6. The agent requests a refund; an admin approves it; the agent cannot approve their own; the customer is emailed; no payment is processed.
7. The agent resolves the ticket, and the analytics summary reflects it in resolution time and the daily count.
8. `GET support/tickets/{id}/audit` shows every one of those actions with actor, timestamp, before/after and reason.
9. Grafana shows the four support instruments; the blackbox exporter probes the new host; `docs/support-ticketing.md` is written and linked from the README.

---

## 13. Deferred / open (not this iteration)

- **Chatbot transcripts** — `TicketSource.Chatbot` and `EscalationTranscript` exist; the producer arrives with Feature 3.1/3.2.
- **Fraud-flagged tickets** — `TicketSource.FraudFlag` is reserved. There is no FraudDetection service (reverted in `6ae4879`), so Feature 3.4's "flags to Support" hop and its fraud dashboard are out of scope.
- **SLA timers and auto-escalation** — a Quartz job over `OpenedOnUtc`/`FirstRespondedOnUtc` is the natural follow-up; both timestamps are recorded from Milestone B, so the data will already be there.
- **Attachments** (a photo of the wrong order) — needs blob storage, which the platform does not have.
- **Restaurant- and driver-facing support** — this feature is customer support only.
- **Gateway role claim** — Milestone I, recommended as its own feature.
- **Full-text search over ticket bodies** — Postgres `tsvector` is the obvious answer; the `GET support/tickets` filters are enough for now.
