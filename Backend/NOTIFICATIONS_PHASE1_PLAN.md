# Feature 1.6 — Notification Service — Implementation Plan

> **Naming note:** this is the *third* module implementation plan (after `RESTAURANTS_PHASE1_PLAN.md` and `ORDERS_PHASE1_PLAN.md`). It covers **Feature 1.6 — Notification Service** from **Phase 1** of `FoodDelivery_ProjectPlan.md`.

> **Channel strategy (decided 2026-07-08):** email is deliberately kept rare and high-signal. **The only email in the whole system is the customer's order-confirmation email, sent when an order is placed.** Everything else — the restaurant's new-order alert and every order status change — is *ephemeral* and will be delivered over **real-time (SignalR) + mobile push**, which is **Phase 2** work (see §7). Sending an email on every status change would be spam; those events belong on live channels.

> **Scope for this iteration (Phase 1):** the **Notifications** module consumes `OrderPlacedIntegrationEvent` and sends **one templated email to the customer**, logging every send to its own database for audit. The send goes through a **channel abstraction** (`INotificationChannel`) with a single `Email` implementation now, so Phase 2 adds `SignalR` and `Push` channels without reworking the core. Email delivery itself stays a **dev-logging sender** (same as the existing invitation email); a real SMTP/SendGrid sender drops in behind the same interface later.

Decisions locked in for this plan:
- **One email, to the customer, on placement.** No restaurant-owner email, no status-change emails — those are Phase-2 real-time/push (§7). This also means **no `Restaurant` replica is needed this iteration** (it was only for the owner email); it moves to the Phase-2 real-time work.
- **Notifications resolves the recipient locally — it never calls another service.** `OrderPlacedIntegrationEvent` carries ids, not an address, so Notifications keeps a minimal `RecipientUser` replica (userId → email, name) fed by `UserRegisteredIntegrationEvent`/`UserProfileUpdatedIntegrationEvent` (consumers already registered). Mirrors how Orders keeps replicas; honours hard rules #4/#5/#9.
- **The `Notification` entity becomes a real audit-log aggregate** (recipient, type, **channel**, subject, status `Pending → Sent | Failed`, error, timestamps). This is the "notification log in PostgreSQL" task and is already channel-aware for Phase 2.
- **A channel abstraction from day one.** `INotificationChannel` (`Channel` + `SendAsync(NotificationMessage)`); only `EmailNotificationChannel` is registered now. A trivial type→channel map routes `OrderConfirmation → Email`. Phase 2 registers `SignalRNotificationChannel` / `PushNotificationChannel` and extends the map — no change to the send pipeline.
- **Retry rides the existing inbox — no bespoke retry table.** A transient send failure throws `Common.Application.Exceptions.ApplicationException`; the inbox leaves the message unprocessed and `ProcessInboxJob` retries. The log row records the outcome.
- **Templates are a small in-code registry**, not a templating engine (Razor/Scriban later).
- Reference implementations to mirror: the existing **`UserInvitedIntegrationEventHandler`** + `SendUserInvitationEmailCommand` (event → command → email), the **Orders replica handlers** (`UpsertCustomerCommand` upsert pattern), and the **Users** aggregate for the notification-log entity.

---

## 0. Dependencies

**None outside this module.** Everything Milestones A–C consume already exists on the bus today: `UserRegistered`, `UserProfileUpdated`, and `OrderPlacedIntegrationEvent`. This plan is fully unblocked. (The deferred Phase-2 status-change delivery *will* need Orders' lifecycle integration events — Orders Milestone D — but that is not part of this iteration.)

---

## 1. Architecture overview

Only the **Notifications** module changes. It has **no HTTP endpoints** (pure event consumer) → **no YARP change, no new permissions**.

| Module | Responsibility this iteration |
|---|---|
| **Notifications** (`fooddeliveryservice_notifications`) | Keeps a `RecipientUser` replica. Turns `Notification` into an audit-log aggregate. On `OrderPlaced`, sends the customer an order-confirmation email via the Email channel and logs it. |
| Users / Restaurants / Orders | **No work here.** |

**New project references** (Notifications currently references only `Users.IntegrationEvents`):
- `Notifications.Infrastructure` → add ref to `Orders.IntegrationEvents` (needed in `ConfigureConsumers`).
- `Notifications.Presentation` → add ref to `Orders.IntegrationEvents` (needed by the handler).

**End-to-end flow (order placed → confirmation email)**

1. Orders publishes `OrderPlacedIntegrationEvent` (already happens today).
2. Notifications' `IntegrationEventConsumer<OrderPlacedIntegrationEvent>` writes it to `inbox_messages`.
3. `ProcessInboxJob` dispatches `OrderPlacedIntegrationEventHandler` (Presentation, idempotent).
4. The handler resolves the **customer** (`RecipientUser` by `CustomerId`) and sends `SendNotificationCommand(OrderConfirmation, Email, tokens)`.
5. The command creates a `Notification` log row (`Pending`), renders the template, dispatches through the `Email` channel, and marks the row `Sent` (or `Failed` + error; a transient failure throws → inbox retries).

---

## 2. `RecipientUser` replica (Milestone A)

Minimal read model in the Notifications DB, keyed by `UserId`, upserted from integration events. Same pattern as Orders' `Customer` replica.

```
Id (= UserId)  Guid
Email          string
FirstName      string
LastName       string
```
Fed by `UserRegisteredIntegrationEvent` (upsert) and `UserProfileUpdatedIntegrationEvent` (email/name sync). **Consumers are already registered** in `NotificationsModule.ConfigureConsumers` — only the handlers + entity + upsert commands are missing.

> Keep **all roles** (no `Customer`-only filter). Only customers are emailed this iteration, but Phase-2 real-time/push must resolve managers/drivers too, and an unfiltered upsert is simpler than a role branch. (If you prefer strict YAGNI, filtering to `Customer` here is acceptable and mirrors Orders — but you'd revisit it in Phase 2.)

### Persistence
- Add `DbSet<RecipientUser>` to `NotificationsDbContext`; one `IEntityTypeConfiguration`; add `ApplyConfigurationsFromAssembly` to `OnModelCreating` (it currently applies only outbox/inbox configs — same gap Orders had).
- Repository `IRecipientUserRepository` (`GetAsync`, `Insert`).
- Migration `Add_Notifications_Recipient_User_Replica` (`recipient_users` table).

---

## 3. Notification-log aggregate + channel abstraction + templated email (Milestone B)

### 3.1 `Notification` aggregate (replaces the stub)
```
Id              Guid
RecipientEmail  string
RecipientUserId Guid?
Type            NotificationType     // enum: OrderConfirmation (only member this iteration)
Channel         NotificationChannel  // enum: Email | Push | Realtime — only Email used now
Subject         string
Status          NotificationStatus   // Pending → Sent | Failed
Error           string?              // populated on Failed
CreatedOnUtc    DateTime
SentOnUtc       DateTime?
```
Guarded domain methods (raise domain events, return `Result` — mirror `User.cs`):
- factory `Notification.Create(recipientEmail, recipientUserId, type, channel, subject, utcNow)` → `Pending`, raises `NotificationCreatedDomainEvent`.
- `MarkSent(utcNow)` → `Pending → Sent`.
- `MarkFailed(error, utcNow)` → `Pending → Failed`.
- `NotificationErrors` (`RecipientEmailEmpty`, invalid transition).

> Notifications is a terminal consumer — it publishes **no** integration events this iteration. Domain events exist only for local audit completeness; keep them minimal.

Migration `Add_Notification_Log` **replaces** the current single-column `notifications` table (stub holds no data → drop-and-recreate is fine).

### 3.2 Channel abstraction (the Phase-2 seam)
```csharp
public sealed record NotificationMessage(
    string RecipientEmail, Guid? RecipientUserId, string Subject, string Body);

public interface INotificationChannel
{
    NotificationChannel Channel { get; }               // Email now
    Task SendAsync(NotificationMessage message, CancellationToken ct);
}
```
- `EmailNotificationChannel` (Infrastructure) wraps `IEmailService.SendEmailAsync`.
- A minimal routing map `NotificationType → NotificationChannel[]` (this iteration: `OrderConfirmation → [Email]`). Phase 2 registers `SignalRNotificationChannel` / `PushNotificationChannel` and extends the map — the send pipeline is untouched.

### 3.3 Generalized email sender
- Extend `IEmailService` with `SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken)`. Re-express `SendInvitationEmailAsync` on top of it so there is one code path (low risk; invitation behaviour unchanged).
- `EmailService` (dev): log subject + body + recipient to Seq inside the existing `ActivitySource` span.
- Add `EmailOptions` (`SectionName = "Email"`, `Provider = "Log" | "Smtp"`, from-address/SMTP fields). Only `Log` is implemented now; the shape lets SMTP/SendGrid be added later without touching callers. (`InvitationEmailOptions.BaseUrl` stays for the activation link.)

### 3.4 Template renderer
- `INotificationTemplateRenderer.Render(NotificationType type, IReadOnlyDictionary<string,string> tokens) → (string Subject, string Body)`; a `switch` over the enum. `OrderConfirmation` tokens: customer first name, restaurant name (from the event if available — otherwise order short id), order short id, subtotal.

### 3.5 `SendNotificationCommand`
```
SendNotificationCommand(string RecipientEmail, Guid? RecipientUserId,
                        NotificationType Type, IReadOnlyDictionary<string,string> Tokens)
```
Handler: resolve channel(s) from the routing map → render template → `Notification.Create(...)` (`Pending`) → `Insert` → `SaveChangesAsync` → `channel.SendAsync(...)` → on success `MarkSent` + save; on exception `MarkFailed(error)` + save **and rethrow as `ApplicationException`** so the inbox retries. Persisting `Pending` first means a crash mid-send still leaves an audit trail.

**Verify (A+B):** `dotnet build` clean; migrations apply; invoking `SendNotificationCommand` writes a `notifications` row transitioning `Pending → Sent`, and the dev channel logs the rendered subject/body to Seq; invitation email still works.

---

## 4. Order-confirmation email (Milestone C)

Consumes `OrderPlacedIntegrationEvent` (**exists today**).

- Add the `Orders.IntegrationEvents` project ref; register `IntegrationEventConsumer<OrderPlacedIntegrationEvent>` in `NotificationsModule.ConfigureConsumers` (`.Endpoint(c => c.InstanceId = instanceId)`).
- `OrderPlacedIntegrationEventHandler` (Presentation): resolve `RecipientUser` by `CustomerId` → `SendNotificationCommand(OrderConfirmation, tokens: firstName, orderShortId, subtotal)`.
- **Missing recipient handling:** if the `RecipientUser` row isn't present yet (the user event may still be in flight), throw `ApplicationException` so the inbox retries; a persistently missing recipient surfaces in the inbox row's `error` (visible for debugging). Never silently drop.

**Verify:** build clean; place an order → **one** `notifications` row (`OrderConfirmation`, `Sent`), body visible in Seq; missing recipient → inbox retry/visible error, not a silent drop; duplicate delivery of the same event → inbox idempotency prevents a second row.

---

## 5. Cross-cutting checklist

- **Hard rules:** recipient resolved from a local replica fed by full-snapshot events (#4, #9); own DB only (#5); no read *API* so no Dapper endpoints (#2 not engaged); saves via `IUnitOfWork.SaveChangesAsync()` (#6); messaging via MassTransit/inbox only (#7).
- **No endpoints, no gateway change, no new permissions** — Notifications is a pure consumer.
- **Observability:** the host already inherits `AddInfrastructure` (OTel + Serilog + Seq + EF/Npgsql/MassTransit). The email send stays inside `EmailService.ActivitySource`; a real SMTP/SendGrid call later is auto-instrumented.
- **Config:** add an `Email` section to `appsettings.json` (empty / `Log` provider) and `appsettings.Development.json`. `InvitationEmail:BaseUrl` unchanged.
- **Migrations** auto-apply via `app.ApplyMigrations()`.
- **Migration analyzer gotcha (from Orders/Restaurants):** after `dotnet ef migrations add`, convert the generated `.cs` to a **file-scoped namespace** and add `[SuppressMessage]` for `CA1861`/`IDE0300` where arrays are seeded (see `Add_Restaurant_Roles_And_Permissions`).

---

## 6. Milestones (each buildable, verifiable, review-sized)

### Milestone A — `RecipientUser` replica *(small)*
1. `RecipientUser` entity + `IRecipientUserRepository` + EF config; `ApplyConfigurationsFromAssembly` in `NotificationsDbContext`.
2. `UpsertRecipientUserCommand` (register) + email/name-sync handler; `UserRegistered` / `UserProfileUpdated` handlers in `Notifications.Presentation` (consumers already registered).
3. Migration `Add_Notifications_Recipient_User_Replica`.
- **Verify:** build clean; migrations apply; register a user → `recipient_users` row; profile update → email/name sync.

### Milestone B — Notification-log aggregate + channel abstraction + email sender *(medium)*
4. Rework `Notification` into the audit-log aggregate (§3.1) + enums + `NotificationErrors` + domain events; migration `Add_Notification_Log`.
5. `INotificationChannel` + `EmailNotificationChannel` + routing map; extend `IEmailService`/`EmailService` with `SendEmailAsync`; add `EmailOptions`.
6. `INotificationTemplateRenderer` + `SendNotificationCommand` + handler (Pending → send → Sent/Failed, rethrow on failure).
- **Verify:** build clean; `SendNotificationCommand` writes a row transitioning `Pending → Sent`; rendered subject/body logged to Seq; invitation email still works.
- *If review size is a concern, split: **B1** = aggregate + migration; **B2** = channel/email/template + `SendNotificationCommand`.*

### Milestone C — Order-confirmation email *(small)*
7. Add `Orders.IntegrationEvents` ref; register the `OrderPlacedIntegrationEvent` consumer.
8. `OrderPlacedIntegrationEventHandler` → customer `OrderConfirmation`; add the template.
- **Verify:** place an order → one `Sent` row + one logged email; missing replica → inbox retry/visible error; duplicate event → one row.

---

## 7. Deferred to Phase 2 — real-time + push (design captured, not built)

The restaurant new-order alert and all order status changes are delivered on **live channels**, not email. This aligns with **Feature 2.2 (SignalR)** and the Phase-2 push line. The channel abstraction (§3.2) is the seam that makes this additive.

**Real-time (web) — SignalR.** Chosen over raw WebSockets (SignalR manages reconnection/fallback/groups) and over SSE (weaker .NET group/scale-out tooling, poor mobile story). The Notification Service (or a dedicated real-time service) consumes the same order events and broadcasts to SignalR groups `user:{userId}` and `restaurant:{restaurantId}` (the connection is authenticated by the existing Duende JWT, so the hub knows who to join). **Scale-out needs a backplane** — Redis (already in the stack) or **Azure SignalR Service**.

**Mobile push.** SignalR only reaches a foreground app; true background push needs the OS pipes, so use **Azure Notification Hubs** fanning out to **FCM (Android) / APNs (iOS)**. Device-token registration + a `PushNotificationChannel` are added then.

**What Phase 2 adds on top of this plan:**
- `Restaurant` replica (restaurantId → managerUserId, name) for owner-addressed notifications.
- Orders **Milestone D** lifecycle integration events (`OrderAccepted/Rejected/ReadyForPickup/Cancelled`) as the triggers.
- `SignalRNotificationChannel` + `PushNotificationChannel`, registered alongside `EmailNotificationChannel`; routing map gains `NewOrderForRestaurant → [Realtime, Push]`, `Order{Accepted,Rejected,Ready,Cancelled} → [Realtime, Push]`.
- SignalR hub + connection/group management + Redis (or Azure SignalR) backplane; Notification Hubs device registry.

---

## 8. Definition of done

- `dotnet build` clean; all new migrations apply on startup.
- Placing an order emails the **customer** a confirmation — the only email the system sends — recorded as one `notifications` audit row that reaches `Sent`, viewable in Seq.
- Every attempted notification is persisted with status and, on failure, an error; transient failures retry via the inbox rather than being lost.
- The recipient address is resolved from a **local replica** — no cross-service calls; the replica stays current (email/name sync on profile update).
- The send pipeline runs through `INotificationChannel`, so Phase 2 adds SignalR/push channels without touching the core.
- No hard-rule violations: own DB only, full-snapshot events consumed, saves via `IUnitOfWork`, messaging via MassTransit/inbox, no endpoints/gateway/permission changes.

---

## 9. Open questions / deferred (not this iteration)

- **Restaurant new-order alert & all status-change notifications** → Phase 2 real-time + push (§7). Deliberately **not** email.
- **Real email provider (SMTP/SendGrid).** Structured for via `EmailOptions.Provider`; only the `Log` sender is implemented now.
- **Exactly-once email.** The inbox gives at-least-once; a rare retry could re-send. A dedupe key on the log (e.g. `(inboxMessageId, type)`) is a later refinement.
- **Templating engine + localization.** In-code string templates now; Razor/Scriban/MJML + per-locale copy later.
- **User notification preferences / opt-out.** No preference model; the single confirmation email sends unconditionally.
- **Read/query API for the notification log.** No HTTP surface added; an admin/support view would add Dapper query endpoints (rule #2) under a new `notifications/**` authorization — out of scope now.
```
