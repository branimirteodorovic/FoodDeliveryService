# Security

> Feature 3.7 — Final Production Hardening. This document is being built one milestone at a time
> (`HARDENING_PHASE3_PLAN.md`). **Milestone A** contributes the authorization guardrails and the
> ownership sweep; **Milestone B** the secrets section; Milestone I turns it into the consolidated
> write-up (OWASP pass, database privileges, TLS boundary, known limitations).

The rule this feature works to: **do not write a security checklist, write a test that fails when
the property is violated.** A checklist is accurate on the day it is written. Everything below that
can be asserted, is — and the "Guardrail" column says where.

## 1. Authorization model

Authorization is **permission-based**, not role-based. A JWT from Duende carries the subject and
nothing about what they may do; each service resolves the caller's permission codes from the Users
service over MassTransit RPC (`GetUserPermissionsRequest`), cached in Redis for five minutes, and
`CustomClaimsTransformation` puts them on the principal. An endpoint names a permission code as its
policy string, and `PermissionAuthorizationPolicyProvider` manufactures the matching
`PermissionRequirement` on demand — which is why the policy name *is* the permission code, and why a
typo in it produces a policy nobody can ever satisfy.

Defence in depth is two layers, not three: the Gateway validates the JWT and rejects anonymous
traffic on every route but the two registration paths; the service validates it again and evaluates
the permission. The Gateway does **not** know permissions, so it cannot shed an unauthorized request
before proxying it.

| Property | Guardrail |
|---|---|
| Every endpoint carries a permission policy or an explicit `AllowAnonymous` | `Common.UnitTests/Security/EndpointAuthorizationTests.cs` |
| The anonymous surface is exactly `users/register` + `users/accept-invitation` | same file — an allow-list asserted in both directions |
| Every policy string names a permission the Users module actually seeds | same file, against `Users.Domain/Users/Permission.cs` |
| Every module's `Permissions` constant names a seeded code | same file |
| The three health probes are anonymous, and nothing else in that mapping is | same file |
| Every YARP route names a defined cluster, and carries an authorization policy | `Common.UnitTests/Security/GatewayRouteTests.cs` |
| Every module path prefix has a gateway route (hard rule #10) | same file |
| The compose and Kubernetes routing tables have not drifted apart | same file, against `deploy/k8s/services/gateway.yaml` |

The tests build each module's **real** route table through the same `AddEndpoints` reflection the
hosts use, so they assert what would actually be served. They run in `Common.UnitTests`, which is
the first entry in CI's project list, so they gate every pull request.

## 2. Ownership and IDOR

Permission codes answer "may this kind of caller do this kind of thing". They do not answer "is this
*their* order", and every endpoint that takes an id needs the second answer too. Two conventions
apply:

- **Ownership is scoped to the caller's id** from the module's `I{Module}Context`, with an
  **administrator bypass** recognized by an admin-only permission the ordinary role never holds
  (`restaurants:create` for Orders and Restaurants, `deliveries:administer` for Delivery,
  `support-tickets:manage` / `support-tickets:administer` for Support).
- **404, not 403, when the resource is not the caller's.** A 403 — or any status that differs from
  the one for an id that does not exist — confirms the resource exists, which is the one thing an
  attacker enumerating ids is trying to learn. Every scoped **read** carries its ownership predicate
  *in the `WHERE` clause* for that reason (§2.2); writes still branch after the read (§2.3).

### 2.1 The sweep

Every endpoint that takes an id, what scopes it, and what a caller who is not entitled to it gets.

| Endpoint | Scoped by | Denied with |
|---|---|---|
| `GET orders/{id}` | ownership **in the SQL**: `AND (@IsAdmin OR o.customer_id = @UserId OR r.manager_user_id = @UserId)` | **404** ✔ |
| `GET orders` | `WHERE` on customer / manager / admin | empty list |
| `POST orders/{id}/cancel` | the order's customer, and only them — no admin bypass | 400 `Orders.NotOwner` |
| `POST orders/{id}/{accept,reject,preparing,ready}` | `OrderOwnership` — the restaurant's manager or admin | 400 `Orders.NotOwner` |
| `GET delivery/deliveries/{id}` | ownership **in the SQL**: `DeliveryAccess.VisibleToCallerSql` — customer, assigned driver, or admin | **404** ✔ |
| `GET delivery/orders/{orderId}/delivery` | the same predicate | **404** ✔ |
| `GET delivery/deliveries` | `WHERE` on driver / admin | empty list |
| `GET delivery/drivers/{id}` | self-or-admin, checked **before** the read | 400 `Drivers.NotSelf` — leaks nothing, the row is never read |
| `GET delivery/drivers/me`, `PUT/PATCH/POST delivery/drivers/me/*` | the route is the caller — no id to confuse | n/a |
| `POST delivery/deliveries/{id}/{accept,reject,picked-up,delivered}` | the offer/assignment is matched to the calling driver in the aggregate | 400 / 409 from the aggregate |
| `GET restaurants`, `GET restaurants/{id}`, `GET restaurants/{id}/menu` | **not** ownership-scoped, by design — the catalogue is readable by any authenticated caller (`restaurants:read` / `menu:read`) | 404 if absent |
| `PUT restaurants/{id}`, all `menu-categories` / `menu-items` writes | `RestaurantOwnership.EnsureCanModify` — owning manager or admin | 400 `Restaurants.NotManager` |
| `GET support/tickets/{id}` | ownership **in the SQL**: `WHERE t.id = @TicketId AND (@IsStaff OR t.customer_id = @UserId)` | **404** ✔ |
| `GET support/tickets/{id}/messages` | the same predicate, plus internal notes filtered in SQL for non-staff | **404** ✔ |
| `GET support/tickets` | the same predicate over the list | empty list |
| `GET support/tickets/{id}/audit` | staff-only (`support-tickets:manage`) — no customer-facing path exists | 404 if absent |
| `POST support/tickets/{id}/{status,assign,claim,unassign,messages}` | staff permission plus the aggregate's own rules (segregation of duties on refunds) | 400 / 403 / 404 per rule |
| `GET support/refund-requests`, `GET support/analytics/summary` | staff-only aggregates over every ticket — deliberately not caller-scoped | n/a |

No handler was found scoping by **nothing**. Everything that takes an id either checks ownership,
filters by it in SQL, or is deliberately open to all authenticated callers (the restaurant
catalogue).

### 2.2 Reads: the predicate is in the query, not in a branch

All four ownership-scoped single-resource reads — Orders, the two Delivery reads and Support's
ticket — now put the ownership predicate **in the `WHERE` clause**. That is what makes the 404
unfakeable: a caller who is not entitled to the row does not get one, so there is no code path on
which a distinguishable "not yours" could be returned instead. Where the check used to sit after
the read (Orders and Delivery returned `Error.Problem` → HTTP 400, which told a caller that a
guessed id was real), it no longer does.

The shared predicates live in one place per module so the read paths cannot drift onto different
definitions of "yours": `DeliveryAccess.VisibleToCallerSql` and `TicketAccess.IsStaff`. Orders
inlines its own — it has a single scoped read.

Covered end-to-end by `Delivery.IntegrationTests/Deliveries/PickupDeliveryTests` — an assigned
driver and an administrator get the tracking view, a bystanding driver gets a 404. Orders has no
equivalent test: its integration suite has one seeded principal, an Administrator, who bypasses
ownership by definition, so a non-owner read cannot be expressed without a second real Duende
identity in the fixture.

### 2.3 Writes still answer 400 on an ownership failure

`OrderOwnership.EnsureCanManage`, `RestaurantOwnership.EnsureCanModify` and `CancelOrder`'s
customer check all return `Error.Problem` → **HTTP 400** with a `NotOwner` / `NotManager` code, which
distinguishes a real id from an absent one exactly as the reads used to. The exposure is the same
class and the reasoning for changing it is the same; it is left standing here because it touches a
dozen endpoints across two shared guards and belongs with Milestone F's error-surface pass (§7.4 of
`HARDENING_PHASE3_PLAN.md`) rather than with the read fix.

## 3. Secrets

The claim being made here is narrow and worth stating precisely: **nothing in this repository is a
credential to anything that exists outside a developer's machine.** Every committed value is valid
only against a throwaway docker-compose or KinD stack. That is not the same as "no secrets are
committed", and pretending otherwise would be the kind of statement this document exists to avoid.

### 3.1 What is committed, and why it is safe

| Value | Where | Why it is safe |
|---|---|---|
| Postgres `postgres` / `postgres` | `appsettings.Development.json` (all nine hosts), `deploy/k8s/base/config.yaml` | A container with no port published outside compose/KinD, holding seeded test data. Milestone C replaces the superuser with per-service roles; that changes the privilege, not the exposure. |
| RabbitMQ and Redis connection strings | same | The same containers, no authentication configured, not reachable off the host. |
| Confidential client secret | see §3.2 | A client-credentials secret for a Duende instance that only ever runs locally. |
| `AdminSeed` password | `Identity/appsettings.Development.json` (`admin`), `config.yaml` (`Admin!23456`) | Seeds the first administrator of a local database. The two differ because outside `Development` ASP.NET Identity enforces its real password rules, so `admin` would fail to seed and leave a cluster with no account to log in with. |
| Grafana admin password | `docker-compose.yml` | The local observability stack. |

**`appsettings.json` — the file that actually ships in the container image — carries none of it.**
Every credential-shaped key in all nine of them is blank, and that is asserted rather than eyeballed:
`Common.UnitTests/Security/SecretHygieneTests.BaseAppSettings_ShipsEveryCredentialBlank` walks each
file and fails on any non-empty value under a key named like a password, secret, key, token or
connection string. `appsettings.Development.json` is never deployed anywhere.

### 3.2 The one secret that is written nine times

`Clients:Confidential:ClientSecret` is the client-credentials secret the **Users** service presents
to Identity's local `api/users` endpoint — the one sanctioned service-to-service HTTP call in the
platform (Hard Rule #4). It appears in nine places:

| Where | Key |
|---|---|
| `Identity/appsettings.Development.json` | `Clients:Confidential:ClientSecret` |
| `Identity/Config.cs` | the `??` fallback literal |
| `Users.Api/appsettings.Development.json` | `Duende:ConfidentialClientSecret` |
| `deploy/k8s/base/config.yaml` | `platform-secrets` → `ConfidentialClientSecret` |
| `{Delivery,Orders,RealTime,Restaurants,Support}.IntegrationTests` | `const ConfidentialClientSecret` in each `IntegrationTestWebAppFactory` |

The duplication is deliberate: hoisting it into a single `.env` would split development configuration
across two mechanisms and break `dotnet run` outside compose.

**Nothing enforces that the nine agree** — this table is the guardrail, which is a weaker guarantee
than a test and is recorded as such. The failure mode is worth knowing before you edit any of them:
change the value in one file and user registration fails with a **401 from Identity's local API**,
three layers below the endpoint you are calling. It reads as "registration is broken", and the file
that broke it appears nowhere in the stack trace.

A test that discovers the copies by pattern and asserts they are identical was built and then
removed as disproportionate — roughly 150 lines of repository-scanning to guard a value whose worst
case is a confusing 401 on a developer machine. If the platform ever gains a credential that is
shared this way *and* authenticates to something real, that decision is worth revisiting.

### 3.3 The gates

| Gate | What it catches | Where |
|---|---|---|
| `gitleaks` over the tracked tree | a credential **value** committed anywhere outside the allowlist | `.gitleaks.toml`, `Backend/tools/secret-scan.sh`, CI job `secrets` |
| ConfigMap key rule | a credential-shaped **key** on the wrong side of the ConfigMap/Secret split | `deploy/k8s/scripts/policy-check.py`, CI job `tools` |
| `SecretHygieneTests` | a credential value in a deployed `appsettings.json` | `Common.UnitTests`, CI job `build-and-test` |

Two scoping decisions behind the scan, both deliberate:

- **The tree, not the history.** The development values above are legitimately committed. Rewriting
  history to purge a throwaway Postgres password would be theatre; what matters is that a *new*
  credential cannot arrive unnoticed, and that is a property of the tree.
- **Tracked files, not the filesystem.** `Backend/tools/secret-scan.sh` scans `git archive HEAD`
  rather than the working directory, because a developer checkout carries `bin/`, `obj/` and — still
  on disk here — the build output of the reverted FraudDetection host. Untracked output cannot have
  been committed, and phantom findings are how a security job gets ignored.

Run it locally exactly as CI does:

```bash
bash Backend/tools/secret-scan.sh
```

The allowlist is two path patterns (`appsettings.Development.json`, `deploy/k8s/base/config.yaml`)
plus the confidential client secret **by value** — the latter because that one string also lives in
five test fixtures and in `Config.cs`, and a path list naming eight files would go stale on the next
suite. Every other high-entropy string in those same files still fails the scan.

### 3.4 What a real environment must supply

The key *names* do not change between compose, KinD and Azure — that is the whole point of the
ConfigMap/Secret split, and it is why no Deployment manifest needs editing to move environments. A
real deployment replaces the contents of `platform-secrets` and nothing else.

| `platform-secrets` key | Configuration path | Azure Key Vault secret name |
|---|---|---|
| `ConnectionStrings__Cache` | `ConnectionStrings:Cache` | `ConnectionStrings--Cache` |
| `ConnectionStrings__Queue` | `ConnectionStrings:Queue` | `ConnectionStrings--Queue` |
| `Database__Identity` … `Database__Support` (eight keys) | mapped per host onto `ConnectionStrings:Database` | `Database--Identity` … `Database--Support` |
| `ConfidentialClientSecret` | `Duende:ConfidentialClientSecret` (Users) and `Clients:Confidential:ClientSecret` (Identity) | `ConfidentialClientSecret` |
| `AdminSeed__Password` | `AdminSeed:Password` | `AdminSeed--Password` |

Key Vault secret names may contain only alphanumerics and hyphens, so the `__` separator becomes
`--`, which the .NET Key Vault configuration provider maps back to `:`. Nothing in this repository
provisions a vault — there is no Azure subscription behind this project (`HARDENING_PHASE3_PLAN.md`
§0) — so the table above is the mapping a deployment would use, stated as such rather than as
something that has been run.

### 3.5 Known limitations

- **A Kubernetes `Secret` is base64, not encryption.** Anyone with `get secret` in the namespace
  reads it. That is acceptable for values worth nothing outside a local cluster, and it is not
  acceptable for the table in §3.4; a real deployment wants the Key Vault CSI driver or
  sealed-secrets, neither of which is in scope here (`KUBERNETES_PHASE2_PLAN.md` was deliberately
  scoped down by the user).
- **No secret rotation exists**, because no secret has a lifetime — none of them authenticate to
  anything durable.
- **Identity has no signing-key store outside Development**, so a restart invalidates every issued
  token. That is Milestone E's work. It is a secrets problem, listed here so this section is not read
  as complete.

## 4. What Milestones A and B do not cover

Named so a reader does not mistake this page for the finished document: database privileges
(Milestone C), security headers / forwarded headers / CORS (D), Identity key management and lockout
(E), input validation and the error surface (F), API documentation reachability (G), supply-chain
scanning (H), and the consolidated OWASP pass, TLS boundary and known-limitations sections (I).
