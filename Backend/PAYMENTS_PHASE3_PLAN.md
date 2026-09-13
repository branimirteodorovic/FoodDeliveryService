# Feature 3.8 — Payments (Stripe) — Implementation Plan

> Twelfth implementation plan, after `RESTAURANTS_PHASE1_PLAN.md`, `ORDERS_PHASE1_PLAN.md`, `NOTIFICATIONS_PHASE1_PLAN.md`, `DELIVERY_PHASE2_PLAN.md`, `REALTIME_PHASE2_PLAN.md`, `CACHING_PHASE2_PLAN.md`, `TELEMETRY_PHASE2_PLAN.md`, `KUBERNETES_PHASE2_PLAN.md`, `LOADTESTING_PHASE3_PLAN.md`, `SUPPORT_PHASE3_PLAN.md` and `HARDENING_PHASE3_PLAN.md`. This one covers **Feature 3.8 — Payments**, which is *not* in `FoodDelivery_ProjectPlan.md` and is added by this plan (§11.3).

> **Scope:** a **new Payments service** that charges the customer's card for an order through **Stripe**, authorizing at placement and capturing when the restaurant accepts, releasing the authorization on reject or cancel, and turning Support's existing refund *request* into a real refund. Backend only. Card entry itself is Stripe.js in the browser — the Angular workstream (`Frontend/FRONTEND_PLAN.md`) — and this plan ships the API surface it will consume.

Decisions locked in for this plan:

- **Stripe, in test mode.** See §0.3. No other provider gives you working API keys, an official current .NET SDK, manual capture and local webhook forwarding without a registered business behind it.
- **Payments is a new service**, `fooddeliveryservice.payments.api` on `:5800`, database `fooddeliveryservice_payments`, routed at `payments/**`. Eighth module, tenth host. Isolating it keeps the Stripe SDK, the API keys and the anonymous webhook ingress off the core order path.
- **Authorize at placement, capture on accept.** A manual-capture `PaymentIntent` at `Pending`, captured on `Accepted`, cancelled on `Rejected`/`Cancelled`. This maps entirely onto integration events **that already exist** — Orders publishes nothing new for the happy path.
- **No synchronous cross-service call is added.** The charge is off-session against a card the customer saved earlier, driven by `OrderPlacedIntegrationEvent`. Hard rule #4 stays intact and the flow is a genuine event-driven saga rather than a disguised RPC.
- **Orders gains an orthogonal `PaymentStatus`, not new `OrderStatus` members.** See §1.2 for why.
- **Stripe is the source of truth; webhooks reconcile.** API responses are optimistic, the `payment_intent.*` webhook commits.
- **Refunds become real**, which **reverses a decision `SUPPORT_PHASE3_PLAN.md` took deliberately.** §9 is largely the cost of retiring that decision honestly.
- Reference implementations to mirror: **Support** for a new service skeleton (most recently added, so its registration points are the freshest), **Delivery** for the distributed lock, **Orders** for replicas fed by integration events.

---

## 0. Prerequisites — what exists, what does not, and what to buy

### 0.1 Already in place — do not rebuild

| Thing | Where | Note |
|---|---|---|
| `Order.Subtotal` | `Orders.Domain/Orders/Order.cs` | `HasPrecision(10, 2)`. Server-side snapshot computed from the menu replica at placement. This **is** the amount to charge (§0.4). |
| `Order.CommissionRate` | same | `HasPrecision(5, 4)`, snapshotted at placement, consumed by nothing. Its comment already says *"the payout math later must use the rate that was in force when the order was placed"*. This plan still does not consume it — see §10. |
| `PaymentMethod` enum | `Orders.Domain/Orders/PaymentMethod.cs` | One member, `CashOnDelivery = 1`, commented *"online payment arrives with the payments work later"*. §8.2 adds `Card = 2`. |
| Four lifecycle integration events | `OrderPlacedIntegrationEvent` (**carries `Subtotal`**), `OrderAcceptedIntegrationEvent`, `OrderRejectedIntegrationEvent`, `OrderCancelledIntegrationEvent` | The whole happy path. Orders publishes nothing new. |
| `RefundApprovedIntegrationEvent` / `RefundRejectedIntegrationEvent` | `Support.IntegrationEvents/` | Published today, consumed only by Notifications. §9 makes Payments the second consumer. |
| `RefundRequest.Amount` capped at `OrderSnapshot.Subtotal` | `Support.Domain/Refunds/RefundRequest.cs` | The refund ceiling is already enforced in the aggregate. §9 does not re-derive it. |
| `IDistributedLock` | `Common.Application/Locking` | Redis `SET NX PX` + token-checked Lua release. §8.1 depends on it absolutely. |
| Anonymous-at-the-edge route precedent | `users/register`, `users/accept-invitation` in the Gateway table | Exactly the shape the webhook route needs (§7.1). |
| Config fail-fast pattern | `HARDENING_PHASE3_PLAN.md` §E, Identity | `ValidateOnStart` so a missing key breaks the host, not the first order. §5.4 reuses it. |

### 0.2 Explicitly NOT available, and this plan does not pretend otherwise

- **There is no payment scaffolding of any kind.** A grep across `Backend/` returns **zero** hits for `Stripe`, `currency`, or a `Money` type, and every hit for `charge`/`capture` is `ConcurrencyStamp`, `ConcurrencyLimiter` or a rate-limiter comment. The only artifacts are the single-member `PaymentMethod` enum, `Order.CommissionRate`, `RefundRequest.Amount` and `Support.Domain/Tickets/TicketCategory.PaymentIssue`.
- **There is no `Money` type and no currency anywhere.** `Subtotal`, `UnitPrice` and `LineTotal` are bare `decimal`. §5.1 introduces the first one.
- **There is no frontend.** Nothing can confirm a Stripe.js `SetupIntent` in a browser. §6.4 says how to demo and test without one.
- **There is no delivery fee, tax, tip or service charge** in the platform, and this feature does not invent them. See §0.4.
- **No role claim is minted in the JWT** (`SUPPORT_PHASE3_PLAN.md` §0). Authorization is permission-based via `IPermissionService`, same as everything else.

### 0.3 Provider selection — why Stripe

| | **Stripe** | PayPal | Adyen | Mollie |
|---|---|---|---|---|
| Test keys without a registered business | **yes, immediately** | yes | no — sales contact | no — account review |
| Official, current .NET SDK | **`Stripe.net`**, first-party | REST-only in practice | yes | community |
| Local webhook forwarding | **`stripe listen --forward-to`** | manual tunnelling | manual | manual |
| Manual capture (auth now, charge later) | **yes** | partial | yes | limited |
| Marketplace split (for §10) | Connect | partial | yes | Connect-like |

Sign up, stay in **test mode**, and you get `sk_test_…` / `pk_test_…` with no verification, no bank account and no possibility of moving real money. That last point belongs in the README — "this project processes payments" invites the question.

Test cards this plan exercises:

| Card | Behaviour | Used by |
|---|---|---|
| `4242 4242 4242 4242` | succeeds | happy path |
| `4000 0000 0000 9995` | declines, `insufficient_funds` | §8.4 |
| `4000 0025 0000 3155` | requires 3-D Secure | §8.5 |
| `pm_card_visa` (a token, not a PAN) | attachable server-side | §6.4 |

`stripe listen --forward-to http://localhost:3000/payments/webhooks/stripe` forwards real test webhooks into the compose stack and prints the signing secret. This is what makes §7 testable without deploying anything.

### 0.4 What is charged

**Charged amount = `Order.Subtotal`. Currency = a configured platform constant (`Payments:Currency`, default `EUR`).**

There is no fee model to honour and inventing one is a different feature. Multi-currency is deliberately not modelled: `Money` carries a currency code (§5.1) so the type is honest, but exactly one value ever flows through it, and the restaurant/customer currency mismatch problem does not exist yet.

### 0.5 PCI scope

**SAQ-A, and it must stay there.** The backend never receives a PAN, CVC or expiry date. It stores Stripe identifiers only — `cus_…`, `pm_…`, `pi_…`, `re_…` — plus card brand and last four digits *for display*. Any endpoint that would accept raw card data is a defect, not a feature. Write this into `docs/payments.md` (§11.2) and `docs/security.md`.

---

## 1. Architecture overview

### 1.1 Module responsibilities

| Module | Responsibility this feature |
|---|---|
| **Payments** (`fooddeliveryservice_payments`) — **new** | Owns `Payment`, `CustomerPaymentProfile`, `Refund`, `StripeEventLog`. Consumes `OrderPlaced`, `OrderAccepted`, `OrderRejected`, `OrderCancelled`, `RefundApproved`, `UserRegistered`. Publishes `PaymentAuthorized`, `PaymentAuthorizationFailed`, `PaymentCaptured`, `PaymentReleased`, `PaymentMethodAttached`, `PaymentMethodDetached`, `RefundSettled`, `RefundFailed`. |
| **Users** | §3 only: the payment permission set, seeded to `Customer` and `Administrator`. |
| **Orders** | `PaymentMethod.Card`, an orthogonal `PaymentStatus`, a guard on `Accept()`, `FailPayment()`, a one-flag `CustomerPaymentProfile` replica, three integration-event consumers. |
| **Support** | §9: `RefundStatus.Settled`/`.Failed`, a consumer for `RefundSettled`/`RefundFailed`, an audit entry per settlement, and the retirement of the "no money moves" prose. |
| **Notifications** | Two new `NotificationType` members + templates + **`NotificationChannelRouter` routes**. A type missing from that router sends nothing and reports success — see the Notifications row in `CLAUDE.md`. |
| **Restaurants / Delivery / RealTime** | **No work.** |

### 1.2 Why `PaymentStatus` is orthogonal to `OrderStatus`

The obvious modelling is a new `OrderStatus.AwaitingPayment` before `Pending`. Do not do it. `OrderStatus` is consumed by Delivery, projected into Support's `OrderTimelineEntry`, pushed as RealTime frames, and asserted by four integration-test suites; a ninth member ripples into all of them for no behavioural gain.

Instead Orders grows a **second, independent** `PaymentStatus` column and exactly one guard: `Order.Accept()` refuses while a card order is not yet `Authorized`. Blast radius: one module. The two dimensions are genuinely orthogonal — a cash order is `PaymentStatus.NotRequired` for its whole life.

### 1.3 End-to-end flow

1. **Once per customer:** `POST payments/payment-methods/setup-intents` → Stripe `SetupIntent` → the browser confirms with Stripe.js → webhook `setup_intent.succeeded` → `CustomerPaymentProfile` records `pm_…` → `PaymentMethodAttachedIntegrationEvent` → Orders replicates the one flag "this customer can pay by card".
2. `POST orders` with `paymentMethod: "Card"`. Orders validates against that replica, writes the order `Pending` / `Authorizing`, publishes `OrderPlacedIntegrationEvent` (already carries `Subtotal`).
3. Payments consumes it and creates a `PaymentIntent` — `capture_method: manual`, `off_session: true`, `confirm: true`, `IdempotencyKey = order-auth-{orderId}` — then publishes `PaymentAuthorized`, or `PaymentAuthorizationFailed` on a decline or `requires_action`.
4. Orders projects `Authorized`. A failure instead drives `Order.FailPayment()` → `Cancelled` + a **distinct** `OrderPaymentFailedDomainEvent` (§8.3), and Notifications emails the customer.
5. The restaurant accepts. `OrderAcceptedIntegrationEvent` → Payments captures (`order-capture-{orderId}`) → `PaymentCaptured` → Orders projects `Captured`.
6. Reject or cancel instead → `PaymentIntents.CancelAsync` → `PaymentReleased`. Nothing was ever charged.
7. Later: an agent requests a refund, a **different** administrator approves it → `RefundApprovedIntegrationEvent` → `Refunds.CreateAsync` (`refund-{refundRequestId}`) → `RefundSettled` → Support marks the request settled and appends an audit entry; Notifications emails the customer.

### 1.4 The two rules that make or break this feature

**Rule 1 — every mutating Stripe call carries an idempotency key derived from a durable id.** `ProcessOutboxJob` and `ProcessInboxJob` are at-least-once. A retry without a key charges the customer a second time. This is why `IPaymentGateway` (§5.2) takes `idempotencyKey` as a *required parameter* on every method rather than computing one internally — an omission has to be impossible, not merely discouraged.

**Rule 2 — every payment mutation takes `IDistributedLock` before the read.** The webhook handler and the outbox handler are two processes racing on one row. Every transition is check-then-act (read status → decide → write) and **no aggregate in this codebase carries an optimistic concurrency token**, so Postgres will not reject the second write. Nothing else is protecting you.

---

## 2. Milestone map

| | Milestone | PR size | Depends on |
|---|---|---|---|
| **A** | Users: the payment permission set | small | — |
| **B** | Payments service skeleton (no business logic) | **large, entirely mechanical** | A |
| **C** | The Stripe seam, `Money`, idempotency discipline | medium | B |
| **D** | Saved payment methods (`SetupIntent`) | medium | C |
| **E** | Webhook ingress | medium, gotcha-dense | C |
| **F** | Authorize on placement | large | D, E |
| **G** | Capture and release | small | F |
| **H** | Real refunds through Support | medium | G |
| **I** | Observability, docs, diagram | medium | H |
| **J** | *Optional* — Stripe Connect marketplace split | — | not in scope |

---

## 3. Milestone A — Users: the payment permission set

**PR size: small.** One constants file, one seeding file, one migration, two test files. Mirrors `SUPPORT_PHASE3_PLAN.md` §2 exactly.

`Users.Domain/Users/Permission.cs`:

```csharp
// Payments (Phase 3, Feature 3.8).
public static readonly Permission ManagePaymentMethods = new("payment-methods:manage"); // customer: attach/detach own cards
public static readonly Permission GetPayments          = new("payments:read");          // customer: own; admin: any — ownership-scoped in the handler
public static readonly Permission AdministerPayments   = new("payments:administer");    // Administrator ONLY — read any payment, force-release
```

**Do not reuse or widen `refunds:approve` to also mean "can see payments."** `SUPPORT_PHASE3_PLAN.md` §2 records that exact trap — a privilege leaking through an unrelated grant — as the reason the `support-*` namespace was carved out in the first place. `payments:administer` is the ownership-bypass code, mirroring `deliveries:administer` and `support-tickets:administer`.

Seeding: `ManagePaymentMethods` + `GetPayments` → `Customer`; all three → `Administrator`. Nothing to `RestaurantManager`, `DeliveryDriver` or `SupportAgent` — an agent who needs to see a payment gets it through the ticket context, not a direct grant.

Two things the mirror of `SUPPORT_PHASE3_PLAN.md` §2 does not carry over:

- **`Administrator` is not in `Role.Assignable`**, so it cannot be provisioned and its grants cannot be asserted through `ProvisionUserRequest` the way `SupportPermissionTests` asserts the agent's. `PaymentPermissionTests.Seeding_Should_GrantAdministratorAllThreePaymentCodes` reads the seeded `role_permissions` rows directly over `IDbConnectionFactory` instead (`const` SQL, per `SqlParameterisationTests`). The other two cases — `Customer` gets its two codes and not `payments:administer`, `SupportAgent` gets none of the three — do go through provisioning.
- **The migration EF generates needs hand-editing** before it builds: warnings are errors, so it wants the file-scoped namespace and the two `SuppressMessage` attributes (`IDE0300`, `CA1861`) on `Up`/`Down` that every other seeding migration here carries. Copy the shape from `20260828131508_Add_Support_Ticket_Administer_Permission.cs`.

Shipped: `20260908074005_Add_Payment_Permissions`, `PermissionTests.PaymentCodes_ShouldBeTheirOwnNamespace` (Users.UnitTests 26/26), `PaymentPermissionTests` (3 cases, Users.IntegrationTests green against the real Identity on :18080).

---

## 4. Milestone B — Payments service skeleton

**PR size: large, and it contains no business logic at all.** It ships a service that starts, migrates, passes health probes and serves empty API docs. Mirror `src/Modules/Support/` and `src/API/FoodDeliveryService.Support.Api/` — Support is the most recently added service, so its registration points are the least stale.

### 4.1 Projects

Five under `src/Modules/Payments/`: `…Payments.Domain`, `…Payments.Application`, `…Payments.Infrastructure`, `…Payments.Presentation`, `…Payments.IntegrationEvents`, plus `…Payments.UnitTests` and `…Payments.IntegrationTests`. `Application` and `Presentation` must each expose an `AssemblyReference.Assembly`.

Host `src/API/FoodDeliveryService.Payments.Api/`: `Program.cs`, `Dockerfile` (explicit per-csproj `COPY` list in the restore layer), the appsettings pair, `Properties/launchSettings.json`, `GlobalSuppressions.cs`, `Extensions/MigrationExtensions.cs`, `Extensions/DuendeHealthChecksBuilderExtensions.cs`, `Middleware/GlobalExceptionHandler.cs`, `OpenTelemetry/DiagnosticsConfig.cs`. Its only `ProjectReference` is the module's Infrastructure.

### 4.2 `Program.cs`

Copy the call sequence from `src/API/FoodDeliveryService.Support.Api/Program.cs` verbatim and swap the names. Two traps:

- Module hosts do **not** call `AddHostTelemetry` — `AddInfrastructure` does it (`Common.Infrastructure/InfrastructureConfiguration.cs:271`). Only Gateway and Identity call it directly.
- Module hosts must **not** acquire `UseEdgeCors` or `UseEdgeForwardedHeaders`. `SecurityHeaderCoverageTests` fails a non-Gateway host that has either.

`ApiDocumentationCoverageTests` matches on literal text and on ordering: `app.UseAuthentication();` must appear textually before `app.MapApiDocumentation(allowAnonymous: app.Environment.IsDevelopment());`.

### 4.3 Registration points

Every row here has a test that fails if it is skipped. This table is the milestone.

| Where | What |
|---|---|
| `FoodDeliveryService.Api.slnx` | **Hand-edit** — nothing is auto-discovered. Folders `/src/Modules/Payments/` (5 projects) and `/src/Modules/Payments/test/` (2), host under `/src/API/`. |
| `docker/postgres/init/01-roles.sql` | `fds_payments_app` / `fds_payments_owner`, the database, the service key in **all five** `ARRAY[…]` literals (~lines 44, 72, 81, 90, 96), and a per-database grant block copying the Support one. |
| `Common.UnitTests/Security/DatabaseRoleTests.cs` | Add to the hardcoded `Hosts` tuple array. It then asserts the `\connect` set matches the host set exactly, the app/owner connection-string split, `platform-secrets` keys, the k8s mapping, migration pool ≤ 2, and total bounded pools < `max_connections - 20`. |
| `docker-compose.yml` + `docker-compose.override.yml` | Service on **5800/5801** (next free after Support's 5700), env + the four user-secrets/https volume mounts. |
| `docker/prometheus/prometheus.yml` | Blackbox targets for `/health/live` (~line 48) and `/health/ready` (~line 82). |
| `deploy/k8s/services/payments.yaml` | Copy `support.yaml`. Four `secretKeyRef` envs, all three probes, requests+limits, `drop: ["ALL"]`, no `ASPNETCORE_HTTPS_PORTS`. **`runAsUser` stays 1654** — see §4.5. |
| `deploy/k8s/base/config.yaml` | `Database__Payments` / `DatabaseMigrations__Payments` in `platform-secrets`. |
| `deploy/kind/scripts/kind-up.{sh,ps1}`, `deploy/k8s/scripts/cluster-smoke.sh` | `IMAGES` and `DEPLOYMENTS` arrays. |
| `.github/workflows/ci.yml` | The hardcoded `projects=( … )` array (lines 57–67). Unit suite only — Identity-dependent integration suites run in the `cluster` job by design. |
| Gateway — **both** tables | `src/API/FoodDeliveryService.Gateway/appsettings.Development.json` **and** the `appsettings.Kubernetes.json` ConfigMap embedded in `deploy/k8s/services/gateway.yaml` (dashes not dots in the k8s address). `GatewayRouteTests` asserts both and their non-drift. |
| `Common.Presentation/Documentation/ApiDocumentation.cs` | A `Payments` descriptor **added to `All`**, with a matching anonymous `docs/payments/**` Gateway route. |
| `Common.UnitTests/…csproj` | `ProjectReference` to `Payments.Presentation` (needed by `EndpointAuthorizationTests` and `ValidatorCoverageTests`). |
| `ValidatorCoverageTests` | A `using PaymentsApplication = …` alias and a `new("Payments", PaymentsApplication.AssemblyReference.Assembly, DeclaresRequests: **false**)` entry — §4.5. |
| `EndpointAuthorizationTests`, `OpenApiDocumentTests` | **Not in the original table, and both fail without an entry.** `ModuleSurfaces` (`HasHttpSurface: false`), `ModulePermissionSets` (`typeof(PaymentsApplication.Permissions)`), and `Modules` (`HasDocumentedOperations: false`). |
| `DatabaseRoleTests` + `deploy/k8s/base/postgres.yaml` + `docker-compose.yml` | The ninth database costs 22 connections and breaks the server's headroom budget — §4.5. |
| `deploy/README.md`, `docs/api-documentation.md` | Service list and URL table. |

### 4.4 `PaymentsModule.cs`

Same shape as `SupportModule.cs`: `AddDomainEventHandlers()` → `AddIntegrationEventHandlers()` → private `AddInfrastructure(configuration)` → `AddEndpoints(Presentation.AssemblyReference.Assembly)`. The private half registers `PaymentsDbContext` (Npgsql + snake_case + `InsertOutboxMessagesInterceptor`), `IUnitOfWork`, one `AddScoped` per repository, `IPermissionService`, `IPaymentsContext`, and the two Quartz option/configure pairs for outbox and inbox. `ConfigureConsumers()` registers one `IntegrationEventConsumer<T>` per subscribed event with `.Endpoint(c => c.InstanceId = instanceId)`, plus the mandatory `AddRequestClient<GetUserPermissionsRequest>()`.


### 4.5 What Milestone B shipped, and where it departed from §4.1–4.4

Ports **5800/5801** in compose, local launch profile **5109/7210** (Support's 5108/7209 are already
shared with Notifications; a third copy was not worth adding). One migration,
`20260908114955_Add_Payments_Outbox_And_Inbox` — no aggregate, so it creates the outbox and inbox
tables and their four dispatch/correlation indexes and nothing else. `dotnet build` clean,
`Common.UnitTests` 458/458, `policy-check.py` and `kubeconform -strict` green.

**The `runAsUser` instruction in §4.3 was wrong and is not implemented.** 1654 is not Support's
choice — it is `APP_UID` in `mcr.microsoft.com/dotnet/aspnet`, it owns `/app` in the published
layer, and every one of the nine existing manifests uses it. A distinct UID is a pod that cannot
read its own binaries. Isolation between services comes from the namespace, the per-service database
roles and the network; the container UID was never carrying it.

**The ninth database broke the connection budget, and the fix was the server ceiling.** Payments adds
2 × 10 app + 2 migration = 22 connections, taking the bounded worst case from 176 to 198 —
`DatabaseRoleTests.BoundedConnectionTotal_FitsInsideTheServersMaxConnections` requires 20
connections of headroom under `max_connections`, so 198 against 200 fails. The pools are already as
small as they usefully go (and §4.3's own row forbids raising the migration pool above 2), so
`max_connections` went **200 → 250** in both `deploy/k8s/base/postgres.yaml` and
`docker-compose.yml`. `docker-compose.yml`'s existing comment had predicted exactly this: *"bounding
the pools alone leaves no room for the tenth service this repo will add."* A tenth database costs
another 22 and still fits.

**Three coverage suites carry Payments as an explicit "nothing here yet", not as `true`.** §4.3 asks
for `DeclaresRequests: true`, which cannot hold in a milestone whose whole point is that there is no
business logic: the flag's own vacuity guard fails on an empty assembly. So Payments is registered
in `ValidatorCoverageTests` (`DeclaresRequests: false`), `EndpointAuthorizationTests`
(`HasHttpSurface: false`) and `OpenApiDocumentTests` (`HasDocumentedOperations: false`), each with
the reason written next to it, exactly as Notifications and RealTime are. **Milestone D flips all
three to `true` in the same change as its first endpoint** — and each one fails loudly if it forgets,
which is the point of registering them now rather than later.

Four things that cost time and are not in §4.1–4.4:

- **`Payments.Infrastructure` must reference `Users.IntegrationEvents` even though it consumes
  nothing.** `GetUserPermissionsRequest` is declared there, and both `PermissionService` and
  `AddRequestClient<>` name it. Omitting it is two compile errors, and the host's `Dockerfile` needs
  the matching `COPY` line or the Release image fails to restore.
- **The generated migration needs the same hand-edit as the seeding migrations in §3** — file-scoped
  namespace, or `IDE0161` fails the build. That applies to an entity-free migration too; the
  `.Designer.cs` and the snapshot are fine as generated.
- **The host does not call `AddModuleDiagnostics`.** `PaymentsDiagnostics` does not exist until
  §11.1, and there is a comment in `Program.cs` saying so. Adding the registration first would
  register a name nothing records into — harmless, but indistinguishable from the failure §11.1
  warns about.
- **The CI array lists `Payments.UnitTests` while it is empty.** `dotnet test` exits 0 on an
  assembly with no tests, so the entry asserts nothing until Milestone C's `Money` tests land. It is
  listed now because the array is hand-maintained and a suite added in a later pull request than its
  project is a suite nobody runs in between.

The `payments/**` routes fall through `RateLimitRoutePolicy` to the default Read/Write tiers, which
is correct for now — the webhook path's exemption is §7.3's job and must not be pre-empted here.

Verified beyond the build: `01-roles.sql` was run into a throwaway `postgres:17`, which created
`fooddeliveryservice_payments` owned by `fds_payments_owner`; the host then booted against it and
reported `/health/ready` **Healthy on all five checks** — including `masstransit-bus`, which
confirms a `ConfigureConsumers` registering no consumer at all still yields a working bus. The
compose and KinD stacks still need the `.containers/db` / PVC wipe from §13.5 before they will see
the new database.

---

## 5. Milestone C — the Stripe seam, `Money`, and idempotency discipline

**PR size: medium. No endpoints.** This is the abstraction that makes every later milestone testable without a network.

### 5.1 `Money` — the repo's first

`Payments.Domain/Money.cs`:

```csharp
public sealed record Money(decimal Amount, string Currency)
{
    public static Result<Money> Create(decimal amount, string currency) { /* amount >= 0, ISO-4217 3-letter */ }

    // Stripe works in integer MINOR units. An implicit (long) cast truncates and silently
    // undercharges — 12.99 becomes 1298. Round explicitly, away from zero.
    public long ToMinorUnits() => (long)Math.Round(Amount * 100m, MidpointRounding.AwayFromZero);
}
```

Zero-decimal currencies (JPY, KRW) do not divide by 100. §0.4 fixes the platform to one currency, so hard-code the exponent and leave a comment saying what would have to change — do not build a currency-exponent table nothing exercises.

### 5.2 `IPaymentGateway`

`Payments.Application/Abstractions/Payments/IPaymentGateway.cs` — `AuthorizeAsync`, `CaptureAsync`, `ReleaseAsync`, `RefundAsync`, `CreateCustomerAsync`, `CreateSetupIntentAsync`.

**Every method takes an explicit `string idempotencyKey`.** Making it a required parameter rather than an internal detail is the whole point: it is the difference between "someone forgot" being possible and being a compile error. Key formats, fixed here and used nowhere else:

| Operation | Key |
|---|---|
| Authorize | `order-auth-{orderId}` |
| Capture | `order-capture-{orderId}` |
| Release | `order-release-{orderId}` |
| Refund | `refund-{refundRequestId}` |
| Create customer | `customer-{userId}` |

All derived from durable ids that survive a retry. Never a `Guid.NewGuid()`, never a timestamp.

### 5.3 `StripePaymentGateway`

`Payments.Infrastructure/Stripe/StripePaymentGateway.cs`, using `Stripe.net`, passing `new RequestOptions { IdempotencyKey = idempotencyKey }` on every mutating call.

Error mapping is the subtle part, because it decides whether the outbox retries:

| Stripe outcome | Maps to | Why |
|---|---|---|
| `card_error` (decline, expired, insufficient funds) | `Result.Failure(PaymentErrors.Declined(reason))` | A business failure. Retrying will decline again. |
| `invalid_request_error`, `idempotency_error` | `Result.Failure` | Our bug. Retrying will not fix it; alert instead. |
| `authentication_error` | `Result.Failure(PaymentErrors.GatewayNotAuthenticated)` | The API key was rejected — configuration, not the card. Added in the build; it is the symptom of a missing user secret and deserved its own error rather than a generic one. |
| `api_connection_error`, `api_error`, `rate_limit_error`, HTTP 5xx | **throw** `Common.Application.Exceptions.ApplicationException` | Transient. ~~The outbox/inbox job must retry.~~ **It does not — see §5.6.** The throw still matters: it records the failure as a fault on the message row rather than letting it be mistaken for a decline. |

### 5.4 Options and secrets

`StripeOptions` (`SecretKey`, `WebhookSecret`, `Currency`) with `ValidateOnStart` — the fail-fast pattern `HARDENING_PHASE3_PLAN.md` §E established for Identity. A missing key breaks the host at boot, not the first order.

Values go to user-secrets in Development and `platform-secrets` in Kubernetes; nothing lands in `appsettings.json`.

**Add `sk_test_`, `sk_live_`, `rk_live_` and `whsec_` to the patterns in `Common.UnitTests/Security/SecretHygieneTests.cs`.** A live key committed to a public portfolio repo is the worst outcome available in this feature. Stripe's own scanner will find and revoke it, usually before you notice, and the revocation email is not a good look in a repo a recruiter is reading.

### 5.5 `FakePaymentGateway`

In the test project. Deterministic, scriptable per test (`succeed`, `decline`, `require_action`, `throw_transient`), and **records every call with its idempotency key** — that recording is what §11.1's double-charge regression test asserts against.

### 5.6 What Milestone C shipped, and where it departed from §5.1–5.5

`Stripe.net` **52.4.1**, referenced by `Payments.Infrastructure` and by nothing else. No endpoints, no
aggregate, no migration. Green: `dotnet build` on the solution, `Payments.UnitTests` **69/69** (new —
the §4.5 CI entry now asserts something), `Payments.IntegrationTests` **10/10** (the fake's own
tests), `Common.UnitTests` **459/459** (458 + the new Stripe scan), `policy-check.py`. `kubeconform`
is not installed on this machine and was not run; the manifest change is two `secretKeyRef` env
entries copied from the four already there, and both changed YAML files were parsed.

**Nine departures and constraints, in the order they cost time.**

- **§5.3's "the outbox/inbox job must retry" is false, and §8/§9 must not be built on it.**
  `ProcessInboxJob` was already known not to retry (§8.1 says so). `ProcessOutboxJob` does not
  either: its per-message `catch` writes the exception into the message's `error` column and then
  marks the row `processed_on_utc` in the same transaction as the successes. So a transient Stripe
  fault does not come back around — the payment is stranded until something re-drives it, which is
  the reconciling webhook (§7) and nothing else. Throwing is still the right response, because it is
  what distinguishes a provider fault from a decline in the message row and the logs; it is just not
  a retry. **§8's terminal-state no-ops and §7's reconciliation are therefore load-bearing, not
  belt-and-braces.**

- **`Money` has a private constructor, not the positional form in §5.1.** A positional record's
  primary constructor is public, so `new Money(-5m, "XYZ")` compiles and skips `Create` — which
  makes the type decorative. Same shape as every aggregate here. `Create` allows **zero** (a
  running "refunds settled so far" total starts there); a charge being strictly positive is the
  `Payment` aggregate's rule in §8, not this type's.

- **`Currency` is not on `StripeOptions`.** §5.4 puts it there; it cannot go there. The handler that
  turns `Order.Subtotal` into a `Money` is an Application-layer handler, and Application cannot
  reference Infrastructure. It is `PaymentsOptions.Currency` in
  `Payments.Application/Abstractions/Payments/`, bound from `Payments:Currency` — which is the key
  §0.4 named in the first place. Validated at boot through `Money.Create`, so the startup check and
  the per-amount check cannot drift. The gateway never reads it: the amount carries its own
  currency, which is most of the point of having the type.

- **`StripeOptions` lives in Infrastructure and is invisible to Presentation — a constraint on §7.**
  Infrastructure references Presentation, not the reverse, so an endpoint cannot see `StripeOptions`
  *or* any Stripe SDK type. §7.2's sketch, which calls `EventUtility.ConstructEvent` inside the
  endpoint, does not compile in this solution. The webhook signature check has to sit behind an
  Application-layer abstraction implemented in Infrastructure, with the endpoint reading the raw body
  and handing over bytes plus the signature header. The note is repeated at §7.2.

- **Presence and shape are two different checks, deliberately split.** Presence is
  `AddRequiredConfiguration(builder.Configuration, builder.Environment, "Stripe:SecretKey",
  "Stripe:WebhookSecret")` in `Program.cs` plus the `IStartupValidator.Validate()` call after
  `Build()` — the Feature 3.7 Milestone E pattern, which **skips Development**. That carve-out is
  load-bearing: a `docker-compose up` without Stripe user secrets, and every integration-test host
  from §6 onwards, must still start. Shape is `StripeOptionsValidator`, which runs everywhere and
  never skips. It **refuses a live key (`sk_live_`/`rk_live_`) in every environment** — an addition
  beyond §5.4, because §0.3 and the README both claim this platform cannot move real money, and a
  claim with no enforcement behind it is a claim. It also rejects a publishable `pk_` pasted into the
  secret slot, which is the common version of that mistake and otherwise surfaces as an opaque 401.

- **The KinD cluster needed placeholder Stripe values or it stops deploying.** With presence enforced
  outside Development, `ASPNETCORE_ENVIRONMENT=Kubernetes` means the Payments pod CrashLoopBackOffs
  without the two keys, and `cluster-smoke` fails on it. Committing a real test key is out of the
  question (§5.4's own reasoning). So `platform-secrets` carries
  `Stripe__SecretKey: sk_test_kind_local_no_stripe_account` and
  `Stripe__WebhookSecret: whsec_kind_local_no_stripe_account`, labelled as placeholders, with
  `payments.yaml` mapping both. Any call that reaches Stripe from that cluster fails with a visible
  `invalid_api_key`, which is the honest behaviour for a cluster that has no Stripe account.
  `StripeOptionsValidatorTests` pins that these two placeholders still pass the shape rules, so a
  later tightening cannot silently break the cluster gate.

- **The namespace `…Infrastructure.Stripe` collides with the SDK's root namespace.** Inside it, a
  plain `using Stripe;` binds to the enclosing namespace and every SDK type stops resolving.
  `using global::Stripe;` is the fix and it is needed in every file in that folder. Same trap as the
  `Delivery` class versus the Delivery module namespace.

- **A decline needs to carry its reason as data, so there is a `PaymentDeclinedError`.** §5.3 says a
  decline is a `Result.Failure`; §8.4 says the resulting integration event carries the bounded reason
  code. An `Error` has a code, a description and a type, none of which is a place to put a reason a
  consumer can switch on — and encoding it into the code string means parsing it back out. So
  `PaymentErrors.Declined(reason)` returns a `PaymentDeclinedError : Error` with a `Reason` property,
  and **§8 recovers it with `if (result.Error is PaymentDeclinedError declined)`** rather than
  inventing a second channel. The five reasons themselves are `PaymentFailureReason` in the Domain,
  declared here rather than in §8 because mapping Stripe's decline codes onto them is the seam's job.

- **3-D Secure is resolved in the seam, not in §8's handler.** `AuthorizeAsync` sets
  `ErrorOnRequiresAction = true`, so an off-session card that wants a challenge comes back as a
  `card_error` with code `authentication_required` instead of leaving a `requires_action` intent
  hanging until it expires. §8.5's instruction — "treat it as an authorization failure with reason
  `authentication_required`" — is therefore already true by the time a handler sees the result.

**Two smaller things worth knowing.**

- **§5.4's "add the patterns to `SecretHygieneTests`" had nowhere to add them.** That suite has no
  pattern list — it asserts that credential-shaped *keys* in `appsettings.json` are blank. So the
  check is a new test, `NoStripeCredential_IsCommittedAnywhereUnderBackend`, scanning the text files
  under `Backend/` for `sk_`/`rk_`/`whsec_` followed by **16 or more unbroken alphanumeric
  characters**. The length floor is what lets the placeholders, the prose in this plan and the
  literals in the validator coexist with the rule. `.gitleaks.toml` gains a matching
  `stripe-webhook-secret` rule — gitleaks' defaults already cover `sk_`/`rk_` but carry nothing for
  `whsec_`, which is the value that lets anyone forge a `payment_intent.succeeded`. Consequence for
  future test fixtures: **a realistic-looking fake key fails the build**, so use segments shorter than
  16 characters (`sk_test_not_a_real_key`).

- **`Payments.UnitTests` references Application and Infrastructure, not just Domain.** Two of the
  three things this milestone ships live above the Domain, and CI runs this suite while it does not
  yet run the integration one. `Payments.Infrastructure` grew a second `InternalsVisibleTo`.
  Precedent: `Restaurants.UnitTests` already references its Application, `RealTime.UnitTests` its
  Infrastructure. `FakePaymentGateway` stays in `Payments.IntegrationTests` where §13.2 needs it —
  and it **replays a repeated idempotency key with the first response, the way Stripe does**, marking
  the call `Replayed` rather than hiding it, so §13.2 can tell "the handler retried harmlessly" from
  "the customer was charged twice".

**Local setup**, since nothing writes a Stripe key to a file:

```bash
dotnet user-secrets set "Stripe:SecretKey" "sk_test_..." --project src/API/FoodDeliveryService.Payments.Api
dotnet user-secrets set "Stripe:WebhookSecret" "whsec_..." --project src/API/FoodDeliveryService.Payments.Api
```

---

## 6. Milestone D — saved payment methods

**PR size: medium.**

### 6.1 `CustomerPaymentProfile`

Aggregate: `CustomerId`, `StripeCustomerId`, `DefaultPaymentMethodId`, `Brand`, `Last4`, `ExpiryMonth`, `ExpiryYear`, `AttachedOnUtc`. Brand/last4/expiry are **display only** and are the outer limit of what may be stored (§0.5).

### 6.2 Stripe customer creation

Consume `UserRegisteredIntegrationEvent` → `CreateCustomerAsync(idempotencyKey: $"customer-{userId}")`. Create it for every registered user; a customer object with no payment method costs nothing and removes a lazy-creation race from the `SetupIntent` path.

### 6.3 Endpoints

| Endpoint | Permission | Notes |
|---|---|---|
| `POST payments/payment-methods/setup-intents` | `payment-methods:manage` | Returns the `client_secret` for Stripe.js. The `SetupIntent` is not the attachment — the webhook is (§7). |
| `GET payments/payment-methods` | `payment-methods:manage` | Own profile only. Dapper. |
| `DELETE payments/payment-methods/{id}` | `payment-methods:manage` | Detaches at Stripe, then locally. |

Publishes `PaymentMethodAttachedIntegrationEvent` / `PaymentMethodDetachedIntegrationEvent` carrying `CustomerId`, `Brand`, `Last4` — **never the `pm_…` id**, which no other service has any business holding.

Orders consumes them into a one-flag replica (`CanPayByCard`) so `PlaceOrderCommandHandler` can reject a `Card` order at placement without asking Payments anything. Hard rule #9, same shape as Orders' existing `Restaurant` replica. The authoritative check still happens in Payments — the replica only avoids accepting an order that is certain to fail.

### 6.4 Demoing and testing without a frontend

Nothing can confirm a `SetupIntent` in a browser yet. Two supported paths:

- **Integration tests:** `FakePaymentGateway` attaches a synthetic method directly.
- **Manual demo:** attach Stripe's `pm_card_visa` **test token** (not a card number) server-side from a `Development`-only endpoint, guarded by `app.Environment.IsDevelopment()`. Delete it when the Angular flow lands.

### 6.5 What Milestone D shipped, and where it departed from §6.1–6.4

Endpoints, all four under `payments/payment-methods` and all four carrying `payment-methods:manage`:
`POST …/setup-intents`, `GET …`, `DELETE …/{id:guid}`, and the Development-only
`POST …/test-cards` from §6.4. Two integration events, `PaymentMethodAttached` and
`PaymentMethodDetached`. Two migrations, `20260912114303_Add_Customer_Payment_Profiles` (Payments)
and `20260912114514_Add_Orders_Customer_Payment_Profile` (Orders). Green: `dotnet build` on the
solution, `Payments.UnitTests` **77/77** (69 + 8 aggregate cases), `Orders.UnitTests` **29/29**
(25 + 4 replica cases), `Payments.IntegrationTests` **18/18** (10 + 8, and the suite's harness is
new), `Common.UnitTests` **459/459**. No manifest, compose or Gateway change: `payments/**` already
routes to the host, and this milestone adds no configuration key or secret.

**The three coverage suites §4.5 registered as "nothing here yet" all flipped to `true` in this
change**, which is what registering them empty was for — `ValidatorCoverageTests`
(`DeclaresRequests`), `EndpointAuthorizationTests` (`HasHttpSurface`) and `OpenApiDocumentTests`
(`HasDocumentedOperations`). `IntegrationEventTopologyTests` also needed a `pay` node in
`DiagramNodes` and the two new edges drawn in the README's C3 section (25 events → 27).

**Seven departures and constraints.**

- **The HTTP surface never carries a `pm_…` identifier, so the aggregate has a surrogate.** §6.3
  writes `DELETE payments/payment-methods/{id}`, which with §6.1's fields can only mean the Stripe
  id — an unbounded provider string in a route, and one that would have to be re-issued the day the
  provider changes. `CustomerPaymentProfile` therefore carries **both** `PaymentMethodId` (a v7
  `Guid`, this platform's identifier, the one in the route and in the response) and
  `StripePaymentMethodId` (`pm_…`, which never leaves the service). The `:guid` route constraint
  comes free with it. §6.3's rule about the *integration events* is unchanged and now also true of
  the API.

- **`Money` is not involved, and neither is `PaymentsOptions`.** Nothing in this milestone has an
  amount. Worth saying because §6 sits between two milestones that are entirely about amounts.

- **`IPaymentGateway` grew two methods and `PaymentIdempotencyKeys` grew three keys.** §5.2 fixes
  five key formats "used nowhere else"; §6.3's endpoints mutate at the provider, and every method on
  that interface takes a key by construction. `AttachPaymentMethodAsync` and
  `DetachPaymentMethodAsync` take `pm-attach-{pm_…}` / `pm-detach-{pm_…}` — keyed on the payment
  method, not the customer, because a customer attaches and detaches repeatedly and
  `pm-attach-{customerId}` would replay the first card's response for 24 hours.

- **`setup-{attemptId}` is the one key not derived from a durable id, deliberately.** Creating a
  SetupIntent moves no money, is driven by a synchronous request nobody retries, and produces an
  object that expires on its own if unconfirmed. Every durable alternative is worse: keying on the
  customer replays a client secret Stripe.js may already have consumed, and keying on a timestamp is
  the anti-pattern §5.2 names. The reasoning is written on the method, because a reader who copies
  it onto an *authorization* would double-charge a card. `CreateSetupIntent_Should_MintAFreshKeyPerAttempt`
  pins it.

- **§7 needs a gateway method this milestone did not add.** The webhook resolves
  `setup_intent.succeeded` to a payment method it did not attach itself, so it needs to *read* one —
  a `GetPaymentMethodAsync`, or an attach that tolerates an already-attached card. `AttachPaymentMethodAsync`
  is the latter as written (Stripe's attach is idempotent for a card already on the customer), so §7
  can reuse it; if it prefers a read, that is a third method rather than a change to this one.

- **Orders' replica is its own table, and F reads it — D only fills it.** §1.1 says "a one-flag
  `CustomerPaymentProfile` replica" and that is what shipped, rather than a column on the existing
  `Customer` replica: the two are fed by different services' events, and sharing the row would make
  a card attached before Orders had seen the registration depend on an event that had not arrived.
  It carries `ChangedOnUtc` and **ignores an event older than the one already applied** — MassTransit
  guarantees no ordering between two messages, and an attach delivered after the detach that
  superseded it would leave a customer being offered a card they removed. Nothing reads
  `CanPayByCard` yet: §8.2 adds `PaymentMethod.Card` and the placement guard in one change, which is
  the right order — the replica must already be populating before a card order can be placed.

- **The Development-only endpoint is invisible to the coverage suites, and that is a consequence
  worth knowing.** `AttachTestPaymentMethod` returns from `MapEndpoint` without mapping anything
  outside Development, and `EndpointAuthorizationTests` builds its route table from a
  `CreateSlimBuilder` whose environment is Production — so the endpoint is simply not in the table
  those tests read. It is written as if it were (a permission, a tag, a summary, a description, a
  `Produces`), because the day it is not Development-only is the day that matters. **Delete it when
  the Angular card flow lands**, as §6.4 says.

**Two things about the test harness**, which is new in this milestone and is what §8 onwards will
extend.

- **Three hosts, built strictly in order, all before the first user is seeded.** Users (publishes
  `UserRegistered`, answers the permissions RPC), Payments, then Orders. Seeding raises
  `UserRegisteredDomainEvent` and the outbox publishes within a second; MassTransit publishes to an
  exchange, so a message with **no queue bound to it is dropped rather than queued** — seed before
  the consumers exist and the payment profile is never created, with nothing reporting an error. All
  three hosts read the same `ConnectionStrings:*` environment-variable keys, so they must build one
  after another and never interleaved.
- **The suite needs Identity on `:18080`** (docker-compose, not a testcontainer) like every other
  integration suite here, which is why CI runs the unit suites only. The Payments container's
  connection string is exposed as a property on the factory rather than read back from
  `ConnectionStrings:Database`: all three hosts write that key, and the last one to build wins.

---

---

## 7. Milestone E — webhook ingress

**PR size: medium. The most gotcha-dense milestone in the plan.** Every item below is something that fails silently or only under load.

### 7.1 The route

`POST payments/webhooks/stripe`, **anonymous**, and its Gateway route must be declared **before** the `payments/{**catch-all}` default-auth route — the same carve-out shape as `users/register`. Both tables (§4.3).

### 7.2 Signature verification needs the raw body

```csharp
app.MapPost("payments/webhooks/stripe", async (HttpRequest request, ISender sender) =>
{
    using var reader = new StreamReader(request.Body);
    string json = await reader.ReadToEndAsync();
    Event stripeEvent = EventUtility.ConstructEvent(
        json, request.Headers["Stripe-Signature"], webhookSecret);
    ...
})
```

Take `HttpRequest` and read the stream yourself. **Never bind a model** — binding consumes the stream, and re-serializing the bound object changes the bytes, so the signature will not verify. The failure mode is a 100% webhook rejection rate that looks like a bad secret.

**The sketch above does not compile in this solution, and Milestone C is why (§5.6).** The endpoint
lives in `Payments.Presentation`, which `Payments.Infrastructure` *references* — so Presentation can
see neither `EventUtility` nor `StripeOptions`, and inverting that reference would put the Stripe SDK
in the assembly every other module's endpoints are modelled on. Put the verification behind an
Application-layer abstraction (`IPaymentWebhookParser` or similar) implemented in
`Payments.Infrastructure/Stripe/`, taking the raw body and the `Stripe-Signature` header and
returning a provider-neutral result. The endpoint still reads the stream itself — that part of the
rule is unchanged and is the one that actually bites.

### 7.3 The rate limiter will shed Stripe's webhooks

`Common.Presentation/RateLimiting/RateLimitRoutePolicy.cs` partitions anonymous callers **by IP**. Every Stripe webhook arrives from a small set of Stripe IPs and therefore shares **one** bucket. A busy minute gets `429`d, Stripe backs off exponentially, and payment state silently lags.

Add an explicit rule exempting this path, alongside `/health/*` and `hubs/**`. **This will not appear in testing and will appear in a demo.**

### 7.4 Dedupe — the MassTransit inbox does not apply

Stripe redelivers on any non-2xx and on its own retry schedule. `inbox_messages` is for MassTransit envelopes and is the wrong table.

Add `stripe_event_log` — Stripe event id (unique index), type, received timestamp, processed timestamp. Insert first; a unique-violation means "already handled", so return `200` immediately. Same two-layered shape as `PlaceOrderCommandHandler`'s idempotency-key handling.

### 7.5 Ordering

Webhooks arrive out of order. `payment_intent.succeeded` can land before `payment_intent.amount_capturable_updated`. **Decide from `paymentIntent.Status` on the payload**, never from arrival sequence, and make every transition idempotent.

### 7.6 Return 2xx fast

Persist the event, raise a domain event, return. Do not call Stripe back inside the request and do not do the aggregate work synchronously — Stripe times out at 20 s and treats a slow endpoint as a failed one.

---

## 8. Milestone F — authorize on placement

**PR size: large.** Split the Orders half into its own commit if the PR gets unwieldy.

### 8.1 The `Payment` aggregate

`OrderId`, `CustomerId`, `Money`, `Status`, `StripePaymentIntentId`, `FailureReason`, timestamps. `PaymentStatus` = `Authorizing | Authorized | Captured | Released | Failed | Refunded`.

Consume `OrderPlacedIntegrationEvent`; **skip cash orders entirely** — no `Payment` row is created for them.

```csharp
// Acquire BEFORE the read — the check-then-act begins at the read, so a lock taken after it
// still lets both callers act on the same stale snapshot. The webhook and the outbox handler
// are two processes racing on this row, and no aggregate here carries a concurrency token.
await using IAsyncDisposable? handle = await distributedLock.TryAcquireAsync(
    PaymentLocks.Payment(orderId), PaymentLocks.Ttl, ct);
if (handle is null) return Result.Failure(PaymentErrors.InProgress);
```

Keys and TTL in **one** shared static (`PaymentLocks`) so the webhook side and the outbox side cannot drift onto different names.

**A lost acquisition must land somewhere a retry exists.** `ProcessInboxJob` does **not** retry — it records the error and marks the message processed. Returning `Result.Success()` strands the payment. Return a failure; the reconciling webhook re-drives it.

**And `ProcessOutboxJob` does not retry either — Milestone C checked (§5.6).** Its per-message
`catch` writes the exception into the row's `error` column and marks it processed alongside the
successes. So there is no retry anywhere on either async leg: a transient Stripe fault, a lost lock
and a handler bug all end the same way, with a recorded error and a payment sitting in whatever state
it was in. The webhook reconciliation in §7 is the only thing that moves it, which makes §7 a
correctness requirement of §8 rather than a refinement of it.

### 8.2 Orders: the payment dimension

- `PaymentMethod.Card = 2`. `PlaceOrderCommandValidator` already parses the enum, so `Card` becomes accepted automatically — add the replica check from §6.3 in the same PR or an unpayable order is placeable.
- `PaymentStatus` enum + column + migration. Add it to both Dapper read handlers (`GetOrder`, `GetOrders`) and their response records. **SQL literals stay `const`** — that is what `SqlParameterisationTests` proves.
- `Order.Accept()` gains a guard returning a new `OrderErrors.PaymentNotAuthorized` while a card order is not `Authorized`. A restaurant accepting inside the authorization window (typically under a second) gets a clean, retryable error.

### 8.3 `FailPayment` must not reuse `Cancel`

`Order.FailPayment(reason, utcNow)` transitions `Pending → Cancelled` and raises a **distinct** `OrderPaymentFailedDomainEvent`.

Reusing `Order.Cancel()` raises `OrderCancelledDomainEvent`, which Payments consumes in order to **release an authorization** — for an order whose authorization just failed. §8.6's terminal-state no-op absorbs that, but relying on it is fragile, and the distinct event is needed anyway so Notifications can say *"your payment was declined"* rather than *"you cancelled your order"*.

### 8.4 Declines

`PaymentAuthorizationFailedIntegrationEvent` carries a **bounded** reason code (`card_declined`, `insufficient_funds`, `expired_card`, `authentication_required`, `gateway_error`) — never Stripe's raw message, which is unbounded free text and would blow up metric cardinality in §11.1.

Those five are already declared as `PaymentFailureReason` in `Payments.Domain/Payments/`, and the
Stripe decline-code mapping onto them shipped with the seam (§5.6). Read the reason off the failure
with a type test — `if (result.Error is PaymentDeclinedError declined) { … declined.Reason … }` — not
by parsing the error code or description.

### 8.5 3-D Secure

An off-session charge can return `requires_action`. Test with `4000 0025 0000 3155`. Treat it as an authorization failure with reason `authentication_required`. A proper on-session retry (notify the customer, have them re-authenticate, resume) is frontend work and is **explicitly out of scope** — say so in `docs/payments.md` rather than leaving a reader to wonder whether it was missed.

Already handled in the seam: `AuthorizeAsync` sets `ErrorOnRequiresAction = true`, so Stripe raises a
`card_error` with code `authentication_required` instead of returning a `requires_action` intent that
would hang until it expired. §8's handler sees an ordinary decline with that reason and needs no
special case (§5.6).

### 8.6 Terminal states are no-ops

Every handler that mutates a payment returns success without acting when the payment is already terminal (`Captured`, `Released`, `Failed`, `Refunded`). Required for outbox idempotency, and it is what makes the `OrderCancelled` path safe after a `FailPayment`.

---

## 9. Milestone G — capture and release

**PR size: small**, given §8.

- `OrderAcceptedIntegrationEvent` → `CaptureAsync(order-capture-{orderId})` → `PaymentCapturedIntegrationEvent`.
- `OrderRejectedIntegrationEvent` / `OrderCancelledIntegrationEvent` → `ReleaseAsync(order-release-{orderId})` → `PaymentReleasedIntegrationEvent`.
- Orders projects both onto `PaymentStatus`.

Both take the lock (§8.1) and both honour §8.6.

---

## 10. Milestone H — real refunds through Support

**PR size: medium**, and about half of it is retracting prose.

### 10.1 Payments side

Consume `RefundApprovedIntegrationEvent` → `RefundsAsync(refund-{refundRequestId})` against the captured intent → publish `RefundSettledIntegrationEvent` or `RefundFailedIntegrationEvent`.

Guards: the payment must be `Captured`, and the refund amount must not exceed the captured amount minus refunds already settled. Support's aggregate already caps the *request* at the order subtotal; this is the second, authoritative check against what was actually taken.

`RefundRejectedIntegrationEvent` is **not** consumed — a rejected request means no money moves, which is already the correct outcome.

### 10.2 Support side

`RefundStatus` gains `Settled = 3` and `Failed = 4` (+ migration). A consumer for `RefundSettled`/`RefundFailed` transitions the `RefundRequest` and appends a `SupportAuditEntry` **in the same transaction as the change it records** — the invariant `SUPPORT_PHASE3_PLAN.md` §C established.

This is Support's first consumption of a Payments event, so `SupportModule.ConfigureConsumers` gains its first `Payments.IntegrationEvents` reference.

### 10.3 Retracting the design decision

`SUPPORT_PHASE3_PLAN.md` decided, deliberately and in writing, that no money moves — `RefundStatus.cs:8` says *"There is no `Paid` member, and its absence is the design"*. **This milestone reverses that, and hard rule #11 says the reversal is part of the same change.**

The claim is asserted in at least eleven places and all of them become false:

| File | What it says |
|---|---|
| `Support.Domain/Refunds/RefundStatus.cs:8` | "no `Paid` member … is the design" |
| `Support.Domain/Refunds/RefundRequest.cs:11-13` | aggregate doc comment |
| `Support.IntegrationEvents/RefundRequestedIntegrationEvent.cs:9-11`, `RefundApprovedIntegrationEvent.cs:10-11` | contract comments |
| `Support.Presentation/Refunds/RequestRefund.cs:40`, `ApproveRefund.cs:18` | endpoint descriptions (**published in the OpenAPI document**) |
| `Support.Application/Analytics/GetSupportSummary/SupportSummaryResponse.cs:91` | analytics comment |
| `Notifications.Presentation/Support/RefundApprovedIntegrationEventHandler.cs:14` | *"no payment is triggered here or anywhere else, because this platform has none"* |
| `Notifications.Infrastructure/Notifications/NotificationTemplateRenderer.cs:53` | *"Approved", never "sent" or "processed"* — the **customer-facing email wording** changes |
| `Notifications.Application/Abstractions/Notifications/INotificationModel.cs:48` | doc comment |
| `Common.Presentation/Documentation/ApiDocumentation.cs:82` | **published API documentation** |
| `docker/grafana/dashboards/business.json:460` | panel description |
| `SUPPORT_PHASE3_PLAN.md` §2 + the Support row in `CLAUDE.md` | the decision itself, and the module contract |

Record the reversal in `SUPPORT_PHASE3_PLAN.md` as a departure — what the literal reading was, why it no longer holds, what replaced it — rather than deleting the original reasoning. A plan that silently drifts from the code is worse than no plan.

### 10.4 Notifications

Two new `NotificationType` members (payment failed, refund settled). Each needs **three** things, not two: the enum member, a template arm, **and** a `NotificationChannelRouter` route. A type missing from that router sends nothing and reports success, leaving a clean outbox/inbox trail and no email — the trap named in `CLAUDE.md`.

---

## 11. Milestone I — observability, docs, diagram

### 11.1 Diagnostics

`PaymentsDiagnostics` over `AppDiagnostics` (**Application layer** — the handlers that record business metrics cannot reference `Common.Infrastructure`), registered with one `builder.Services.AddModuleDiagnostics(PaymentsDiagnostics.Name)`. An unregistered source or meter never errors; it silently records into nothing.

| Instrument | Tags |
|---|---|
| `payments.authorized` | — |
| `payments.captured` | — |
| `payments.failed` | reason (the bounded set from §8.4 — **never** the raw Stripe message) |
| `payments.released` | trigger (`rejected` \| `cancelled`) |
| `payments.gateway.duration` | operation, outcome |
| `refunds.settled` | — |

Emit **last** in the handler so an outbox retry of a failed handler does not double-count.

Grafana panels and Prometheus alerts (authorization failure rate, gateway p95, webhook processing lag) in `docker/grafana/dashboards/` and `docker/prometheus/`. Add the **Prometheus-exported** names — `payments_authorized_total`, `payments_gateway_duration_seconds_bucket` — to `KnownMetrics` in `ObservabilityAssetTests`, or the build fails.

### 11.2 `docs/payments.md`

Flow diagram, the `Payment` state machine, **the two rules from §1.4**, key formats (§5.2), local setup with `stripe listen`, the test-card table (§0.3), PCI scope (§0.5), and an explicit list of what is *not* built: no payouts, no on-session 3DS retry, no multi-currency, no partial captures.

### 11.3 Diagram and project plan

README `### C3 — Event Topology`: every new `*IntegrationEvent` must be named and the edges must match `ConfigureConsumers()` exactly — `IntegrationEventTopologyTests` diffs them and needs a `DiagramNodes` entry for the new module. Add **Feature 3.8** to `FoodDelivery_ProjectPlan.md` (it is not there today) and update the Technology Reference table with Stripe / `Stripe.net`. Add the Payments row to `CLAUDE.md` § Service Responsibilities.

---

## 12. Milestone J — optional: Stripe Connect marketplace split

**Not in scope. Sketch only**, so the next reader knows it was considered rather than missed.

`Order.CommissionRate` is snapshotted at placement for exactly this. Restaurants become Connect Express accounts; each capture carries `application_fee_amount` (the platform's cut, computed from the snapshotted rate) and `transfer_data.destination` (the restaurant's account). Adds Connect onboarding state per restaurant, `account.updated` webhooks, payout reconciliation and a restaurant-facing earnings view — a feature in its own right, not a milestone.

---

## 13. Verification

1. **Unit** — `Payments.UnitTests`: `Money.ToMinorUnits` rounding at the `x.xx5` boundary, the `Payment` state machine including terminal no-ops, the refund ceiling. `Orders.UnitTests`: `Accept()` refused while unauthorized, `FailPayment()` transitions and raises `OrderPaymentFailedDomainEvent`, **cash orders unaffected in every case**.
2. **Integration** — `Payments.IntegrationTests` against Testcontainers with `FakePaymentGateway`: place → authorize → accept → capture; place → decline → order cancelled + email; reject → release; refund approved → settled. **Then force an outbox retry and assert exactly one gateway call with the same idempotency key.** That is the double-charge regression test and it is the one that matters.
3. **Webhook** — a request signed with a known secret succeeds; a tampered body returns 400; a replayed event id returns 200 without acting.
4. **Gates** — `dotnet build` (warnings are errors) then the common suite: `GatewayRouteTests`, `DatabaseRoleTests`, `ValidatorCoverageTests`, `ApiDocumentationCoverageTests`, `SecurityHeaderCoverageTests`, `OpenApiDocumentTests`, `IntegrationEventTopologyTests`, `ObservabilityAssetTests`, `SecretHygieneTests`, `SqlParameterisationTests`.
5. **Manual, end to end** — **wipe `.containers/db` first**: `01-roles.sql` runs only on an empty data directory, so without that the new database is never created and the host fails to start on a confusing permission error. Then `docker-compose up -d`, `stripe listen --forward-to http://localhost:3000/payments/webhooks/stripe`, attach `pm_card_visa`, place a card order, watch `payment_intent.amount_capturable_updated` arrive, accept the order, confirm `payment_intent.succeeded` and the Stripe dashboard's test ledger. Follow it end to end in Jaeger and Seq on a single correlation id — the outbox/inbox correlation work (`TELEMETRY_PHASE2_PLAN.md` §G) already carries it across both async legs.
6. **Kubernetes** — `kubeconform -strict -kubernetes-version 1.31.0 Backend/deploy/k8s`, `policy-check.py`, then `kind-up` + `cluster-smoke`.
