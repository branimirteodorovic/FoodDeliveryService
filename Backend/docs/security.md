# Security

> Feature 3.7 — Final Production Hardening. This document is being built one milestone at a time
> (`HARDENING_PHASE3_PLAN.md`). **Milestone A** contributes the authorization guardrails and the
> ownership sweep; **Milestone B** the secrets section; **Milestone C** the database privilege
> model; **Milestone D** the edge — response headers, forwarded headers and CORS; **Milestone E**
> the identity surface — signing keys, configuration fail-fast, lockout and token lifetimes;
> Milestone I turns it into the consolidated write-up (OWASP pass, TLS boundary, known limitations).

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
| `AdminSeed` password | `Identity/appsettings.Development.json` (`admin`), `config.yaml` (`Admin!234567`) | Seeds the first administrator of a local database. The two differ because outside `Development` ASP.NET Identity enforces its real password rules, so `admin` would fail to seed and leave a cluster with no account to log in with. |
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
- **Identity's signing keys were not persisted anywhere shared**, so a restart invalidated every
  issued token. Milestone E fixed it: the keys, the persisted grants and the Data Protection ring that
  encrypts them now live in the identity database (§6.1). What remains is that the ring itself is
  stored unencrypted at rest — protecting it needs a key from outside the database, and there is
  nowhere here to keep one.

## 4. Database privileges

Until Milestone C every service connected as the PostgreSQL superuser `postgres`, and one server
holds all eight databases. Two consequences, both bad: a SQL-injection or deserialisation bug in
*any* host was a full-platform compromise, and Hard Rule #5 — "never query another service's
tables" — was enforced by code review alone. It is enforced by the server now.

### 4.1 Two roles per service

`docker/postgres/init/01-roles.sql` runs from the Postgres image's `docker-entrypoint-initdb.d` and
creates, for each of the eight service databases:

| Role | Holds | Used by |
|---|---|---|
| `fds_{service}_owner` | Owns the database and everything in it; full DDL. | Exactly one code path: the startup EF Core migration, through `ConnectionStrings:DatabaseMigrations`. |
| `fds_{service}_app` | `CONNECT` on its own database, `USAGE` on `public`, `SELECT/INSERT/UPDATE/DELETE` on its tables and `USAGE/SELECT/UPDATE` on its sequences. No `CREATE`, no other database. | Everything else — `ConnectionStrings:Database`, which the EF Core `DbContext`, the shared `NpgsqlDataSource` behind Dapper and the outbox/inbox jobs all build their pools from. |

Three details in that script carry the weight:

- **`REVOKE CONNECT ON DATABASE … FROM PUBLIC`** is the line that does the isolating. Without it
  every role can open every database and the per-schema grants only decide what it can do once
  inside. With it, `fds_orders_app` is refused before a query is even parsed — so nothing inside the
  Users database has to be got right for Orders to be unable to read it.
- **`ALTER DEFAULT PRIVILEGES FOR ROLE fds_{service}_owner`** grants the app role rights over tables
  that do not exist yet. They are created by a *later* migration, so a plain `GRANT … ON ALL TABLES`
  would cover today's schema and silently miss every table added after — surfacing as a 500 on the
  first request rather than as a failure at boot.
- **The databases are created here, not by EF Core.** `Migrate()` used to create them as a side
  effect of connecting to a database that did not exist. That cannot survive least privilege —
  `CREATE DATABASE` is a cluster-level right no service account should hold — so the script creates
  all eight, already owned by the right role, and `Migrate()` now only ever evolves a schema.
  `ALTER DATABASE … OWNER TO` runs unconditionally afterwards, because a database the container
  entrypoint created from `POSTGRES_DB` already exists and is owned by `postgres`.

The passwords are local-stack credentials in the same category as `postgres`/`postgres` (§3.1), and
they are never written literally — the script builds them with `format()`, so the file contains no
credential string. They **must differ per role**: two roles sharing a password would mean a leaked
app credential also opens the owner account, which is the escalation the split exists to prevent.

### 4.2 One credential per code path

Migrations run in-process, at boot, from the host that then serves traffic, so a single connection
string cannot be both DDL-capable at startup and DML-only afterwards. The split is therefore in
configuration:

- `ConnectionStrings:Database` → `fds_{service}_app`. Everything that serves a request.
- `ConnectionStrings:DatabaseMigrations` → `fds_{service}_owner`. Read by `app.ApplyMigrations()`
  and by nothing else.

`Common.Infrastructure/Data/DatabaseMigrationExtensions.ApplyMigration<TDbContext>()` builds that
context **by hand** rather than resolving it from DI, because the registered one is bound to the app
connection string. It mirrors what each `{Module}Module` registers — same provider, same
`HistoryRepository.DefaultTableName` (which the snake-case convention would otherwise rename,
pointing the migration at a history table that does not exist), same naming convention — and
deliberately omits the outbox interceptor, since nothing raises a domain event during a migration.
Identity takes no `Common.Infrastructure` dependency and repeats those four lines in its own
`ApplyDatabaseMigrationsAsync`.

`DatabaseMigrations` falls back to `Database` when absent. That is what lets the integration fixtures
point a whole host at one superuser Testcontainers connection without knowing the split exists.

**The alternative not taken** is moving migrations into a Kubernetes init container or Job. That is
the right answer for a real production cluster, and it is a change into a workstream the user scoped
out (`KUBERNETES_PHASE2_PLAN.md`), so the two-credential split is what ships.

### 4.3 Pool sizing

`Maximum Pool Size` was tuned in `LOADTESTING_PHASE3_PLAN.md` Milestone F against a measured
`53300: sorry, too many clients already`, and Milestone C did not change it. It added a second,
near-idle pool per host, capped at **2** — all a single sequential migration run can want, and a
privileged pool sitting idle for the life of the process is not something to be generous with.

The bounded worst case moves from `7 × 20 + 20 = 160` to `160 + 8 × 2 = 176`, against the server's
`max_connections=200`. `DatabaseRoleTests.BoundedConnectionTotal_FitsInsideTheServersMaxConnections`
re-derives that from the manifests rather than trusting the comment beside them.

### 4.4 Guardrails

| Property | Where it is asserted |
|---|---|
| The app role cannot `CREATE`, and can read/write tables the owner creates *later* | `Orders.IntegrationTests/DatabasePrivilegeTests` — a real Postgres initialised by the shipped script |
| The app role cannot open another service's database | same |
| Every service database is owned by its own owner role | same |
| No host settings file or `platform-secrets` entry connects as `postgres` | `Common.UnitTests/Security/DatabaseRoleTests` |
| Each host's two connection strings name **its own** service's database and roles | same — a `[Theory]` over the eight hosts, across all three config locations |
| Every Deployment maps `ConnectionStrings__DatabaseMigrations` from the matching Secret key | same |
| The bounded connection total fits under `max_connections` | same |
| The whole stack boots and migrates with the split | `deploy/k8s/scripts/cluster-smoke.sh` — a wrong credential fails `app.ApplyMigrations()` and the pod never reports Ready |

### 4.5 Known limitations

- **The init script runs once, on an empty data directory.** Changing it does nothing to an existing
  cluster. Locally: `rm -rf Backend/.containers/db`. On KinD: delete the StatefulSet's PVC, or run
  `kind-down.sh`. A stale volume presents as every host failing to authenticate as
  `fds_{service}_app` at startup.
- **The KinD ConfigMap is generated, not committed.** `kind-up.sh` builds `postgres-init` from the
  same SQL file compose bind-mounts, so the two environments cannot drift onto different privileges
  — but it also means `kubectl apply -f deploy/k8s/base/` alone does not create it, and neither
  `kubeconform` nor `policy-check.py` sees it.
- **Roles are per service, not per component.** The outbox/inbox jobs and the request path share one
  app credential. Splitting them further would buy no attacker-visible gain: both already run inside
  the same process.
- **Nothing revokes an old role.** The script only creates. A service that is renamed or removed
  leaves its roles and its database behind.
- **The migration credential lives in the pod for the life of the process**, not just for the
  migration. An init container or Job is what removes it from the request-serving container
  entirely; see §4.2 for why that is out of scope here.

## 5. The edge: response headers, forwarded headers, CORS

Milestone D. The only milestone in this feature that changes runtime behaviour on every request, and
the only one whose failure modes are visible in a browser rather than in a log.

The framing matters: **TLS terminates outside anything this repository deploys.** `docker-compose`
and the KinD manifests are HTTP-only by design (`ASPNETCORE_HTTP_PORTS: "8080"`, no certificate in
any pod), and the Kubernetes workstream was deliberately scoped short of an Ingress. So this
milestone does not add a certificate nobody has — it hardens the code for *living behind* a
TLS-terminating proxy, which is a different and more useful job.

### 5.1 Security response headers

One `app.UseSecurityHeaders()` on all nine hosts (`Common.Presentation/Security`), the same shape as
`UseRequestCorrelation()` and for the same reason: nine copies of a header list is how they drift,
and a header present on eight hosts is a header nobody can rely on.

| Header | Value | Why |
|---|---|---|
| `X-Content-Type-Options` | `nosniff` | Stops MIME confusion on a JSON surface. |
| `X-Frame-Options` | `DENY` | The API serves no framable UI. |
| `Referrer-Policy` | `no-referrer` | Nothing here should leak a URL to a third party. |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'` | An endpoint returning `application/json` loads no script, style, image or font. |
| `Content-Security-Policy` (documentation paths) | `default-src 'self'; script-src 'self' 'unsafe-inline'; style-src …` | Swagger UI and Scalar bootstrap from an inline script and inline styles. |
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains`, **HTTPS requests only** | See below. |
| `Server` | *removed* (`AddServerHeader = false`) | `Server: Kestrel` is free reconnaissance. |

Three decisions in there are load-bearing:

- **The headers are written from an `OnStarting` callback, not on the way in.** A header set before
  calling the next middleware is lost the moment something resets the response — which is exactly
  what `GlobalExceptionHandler` and the rate limiter's `429` path do. A middleware that decorates
  only the 200s and leaves every error response bare is the common miss, and the error responses are
  the ones a scanner reads.
- **HSTS is emitted only when the request already arrived over HTTPS.** Over plain HTTP the header
  is not merely useless, it is destructive *here*: a browser that honoured it would pin itself to a
  scheme the local platform does not serve, and the whole stack would be unreachable from that
  browser until its HSTS cache was cleared by hand. This is also the direct reason §5.2 exists —
  behind a TLS terminator, a proxied HTTPS request looks like plain HTTP to Kestrel, so without
  trusted `X-Forwarded-Proto` the header would never be emitted in the one deployment that wants it.
- **The documentation carve-out ships before the documentation does.** No host maps a Swagger or
  Scalar UI today (only `MapOpenApi()`, a JSON document, and only in Development); Milestone G adds
  them. Shipping the carve-out first means the UI simply works when it arrives, instead of rendering
  blank in the PR that adds the CSP — which is precisely the self-inflicted breakage this milestone
  must not cause.

The `SecurityHeaders` configuration section can change every value, but no environment does — the
defaults are what all nine hosts run.

### 5.2 Forwarded headers — a live defect, not a precaution

No host configured `UseForwardedHeaders`, and one consequence was already real rather than
hypothetical.

The edge rate limiter partitions **anonymous** callers by `HttpContext.Connection.RemoteIpAddress`
(`RateLimitClient.Resolve`, `docs/rate-limiting.md` §2). Behind any TLS-terminating proxy or ingress
— which is the intended deployment — that address is the *proxy's*. Every anonymous request on the
platform then shares one bucket, and the per-client limit silently degrades into a second global
one. `users/register` is anonymous by design and is exactly the endpoint an abusive client reaches
for. The same substitution puts the proxy's address in every Serilog request log and every trace.

`app.UseEdgeForwardedHeaders()` is added on the **Gateway only** — module hosts sit behind YARP on a
private network and are unreachable from a client (Hard Rule 10), so a second hop of header
rewriting there widens the surface for no gain. It is the **first** middleware in the pipeline,
ahead of correlation, request logging and the limiter, because each of those reads the address or
the scheme it rewrites.

**Nothing is trusted by default, and that is the whole design.** `X-Forwarded-For` is a
client-supplied header: honouring it from an arbitrary sender lets any caller choose its own
rate-limit partition key and its own address in the logs, which is a worse bug than the one this
fixes. The framework's implicit loopback trust is cleared too, so the trust list is exactly what
configuration says.

| Key | Meaning |
|---|---|
| `ForwardedHeaders:KnownNetworks` | CIDR networks to believe — e.g. a cluster's pod CIDR. This is the one a real deployment needs: a proxy's address is not stable, its network is. |
| `ForwardedHeaders:KnownProxies` | Individual proxy addresses. |
| `ForwardedHeaders:ForwardLimit` | How many entries to walk back from the right. `1` = the immediate proxy. Raise it only alongside adding every intermediate hop to the trust list. |

`X-Forwarded-Host` is deliberately **not** forwarded: the Gateway generates no absolute URLs, so an
inbound host header could only ever poison a link the platform does not send.

Two environments, two correct answers, and neither is configured today:

- **KinD / compose:** the Gateway is published directly (NodePort, port mapping), no HTTP proxy in
  front, so no `X-Forwarded-For` is ever sent and an empty trust list is correct — the pod sees the
  real client.
- **Anything with an ingress or a load balancer in front:** set `ForwardedHeaders__KnownNetworks__0`
  to that hop's network, or the limiter is per-platform rather than per-client.

The host logs a **warning** at startup whenever forwarded headers are on with nothing trusted,
because that state is invisible otherwise: everything works, the limiter is simply no longer per
client.

### 5.3 CORS

The Angular SPA (`Frontend/FRONTEND_PLAN.md`) names CORS as a backend prerequisite and nothing
provided it. One named policy, `AddEdgeCors` / `UseEdgeCors`, at the **Gateway only** — it is the
single public entry point, and a per-service policy would be seven copies of one origin list
drifting in front of services no browser can reach.

- **Origins come from `Cors:AllowedOrigins` and are empty in the base `appsettings.json`.** A
  configuration file that ships to every environment must not decide which browsers may talk to the
  platform. `appsettings.Development.json` opens `http(s)://localhost:4200` for the SPA's dev server.
- **`AllowCredentials` is on**, because the SignalR handshake on `hubs/**` needs it. A wildcard
  origin combined with credentials is refused **at startup**, with the offending configuration key
  named — the framework's own failure surfaces later, as a 500 on someone's first login.
- **`X-Correlation-Id` and `Retry-After` are exposed.** Without that list a cross-origin caller can
  read neither: not the correlation id it should quote in a bug report, nor the interval the edge
  limiter sends with a `429`. Both are values the platform expects a client to act on.
- **The policy applies to every proxied route**, including `hubs/**`. No YARP route sets its own
  `CorsPolicy`, so `app.UseEdgeCors()` covers the whole table with one entry — which also means the
  routing table did not have to be edited in both of its copies (`appsettings.Development.json` and
  the `gateway.yaml` ConfigMap).
- **It sits before `UseAuthentication()`.** A preflight is an unauthenticated `OPTIONS` with no
  bearer token; applied after authentication it would be answered with a `401` and the browser would
  never send the real request. Here the middleware short-circuits the preflight before the limiter
  and YARP ever see it.

### 5.4 Guardrails

| Property | Where it is asserted |
|---|---|
| Every header is present on a 200, on a `ProblemDetails` 400, and on a response reset downstream | `Common.UnitTests/Security/SecurityHeadersTests` |
| HSTS absent over HTTP, present over HTTPS | same |
| The documentation carve-out serves a CSP that permits the UI, and only on those paths | same |
| Kestrel's `Server` header is off | same |
| **All nine hosts** call both halves | `Common.UnitTests/Security/SecurityHeaderCoverageTests` — a `[Theory]` over the host directories, so a tenth host is covered the day it is added |
| Forwarded headers and CORS exist on the Gateway and **nowhere else** | same |
| Forwarded headers run before correlation; CORS runs before authentication | same |
| Nothing is trusted unless configured; loopback is cleared | `Common.UnitTests/Security/EdgeForwardedHeadersTests` |
| A non-CIDR entry fails at startup naming the key | same |
| The built policy's origins, credentials, exposed headers and preflight age | `Common.UnitTests/Security/EdgeCorsTests` |
| A wildcard origin with credentials is refused at startup | same |

### 5.5 Known limitations

- **No TLS in this repository**, so the HTTPS branch of the header middleware is exercised by unit
  tests and by no deployment here. That is a deployment-boundary claim, not a code claim.
- **Nothing trusts a proxy today.** The default is correct for both environments this repository
  stands up, and wrong for the environment the milestone was written for — a deployment behind an
  ingress must set `ForwardedHeaders:KnownNetworks` itself. The startup warning is the only thing
  that says so.
- **CORS is closed in every committed environment except Development.** The SPA does not exist yet;
  when it is deployed, its origin is one configuration key.
- **The CSP is not reported on.** There is no `report-uri`/`report-to` endpoint, so a policy that is
  too strict for a future page surfaces as a broken page rather than as a report.
- **No `Permissions-Policy`, no COOP/COEP/CORP.** They govern browser features and cross-origin
  isolation for documents; an API returning JSON has no document to govern. Worth revisiting only if
  a host ever serves a real UI beyond the documentation pages.

## 6. Identity

`FoodDeliveryService.Identity` is the most security-sensitive host on the platform — it is the only
process that ever sees a password, and the signature it produces is the only thing nine other hosts
trust. It had also had the least hardening attention of any of them.

### 6.1 Signing keys: a store, not a directory

`Program.cs` calls neither `AddDeveloperSigningCredential` nor `AddSigningCredential`, and that was
not an oversight — Duende 8 enables **automatic key management** by default, which creates, rotates
and retires signing keys on its own and publishes them through the JWKS document. It needs an
`ISigningKeyStore` to keep them in. With no operational store registered it falls back to a
`FileSystemKeyStore` writing a `keys` directory under the working directory.

Nothing about that fails loudly. The host starts, issues tokens, and serves a discovery document that
validates. The failure arrives later and from a distance:

- **A restart invalidates every issued token.** The container's filesystem is gone, the next boot
  mints a new key, and every JWT signed by the old one now fails signature validation at nine hosts.
- **A second replica is worse**, because it is intermittent. Two pods each hold their own key and each
  advertise their own JWKS. A validator that cached one document rejects tokens minted by the other,
  and which one you get depends on where the load balancer sent the login versus where it sent the
  request. This is the failure mode that looks like anything except a key problem.

In Kubernetes it was visible in the manifest as an `emptyDir` mounted at `/app/keys` — present only
because the non-root container could not create the directory itself, with a comment admitting the
keys were regenerated on restart.

**Milestone E registers Duende's EF Core operational store** (`Duende.IdentityServer.EntityFramework`),
pointed at the same `fooddeliveryservice_identity` database, and deletes the volume. Three things now
live there rather than in a container:

| What | Table(s) | Why it had to move |
|---|---|---|
| Signing keys | `Keys` | Automatic key management's store — the whole of the above. |
| Persisted grants | `PersistedGrants` | The public client sets `AllowOfflineAccess`, so refresh tokens exist; they were in Duende's **in-memory** grant store and died with the process. One-time-only rotation (§6.3) is unenforceable without them. |
| Data Protection key ring | `DataProtectionKeys` | Duende encrypts the keys it persists with the ASP.NET Data Protection ring, so a shared key store behind a per-pod ring is no improvement at all. The same ring protects the three-day invitation activation tokens, which a restart therefore used to invalidate. |

The store connects as the least-privilege `fds_identity_app` account (§4): key management and grant
storage are `INSERT`/`UPDATE`/`DELETE`, and `01-roles.sql`'s `ALTER DEFAULT PRIVILEGES` already grants
the app role rights over whatever the owner's migrations create. The schema itself is migrated by
`fds_identity_owner`, alongside the ASP.NET Identity schema, in `ApplyDatabaseMigrationsAsync`.

**Residual limitation.** The Data Protection ring is stored unencrypted at rest — protecting it needs
a key from outside the database (a certificate, a KMS), and this repository has nowhere to keep one.
Anyone who can read the identity database can therefore decrypt the signing keys. That is a smaller
surface than it sounds — the same reader already has every password hash — but it is a real difference
from a deployment that fronts key material with a vault, and it is why a production Identity would add
`ProtectKeysWithAzureKeyVault` or an explicit `AddSigningCredential` from a certificate.

**Multi-replica is designed for, not proved.** The KinD manifest still runs one Identity pod, so
`cluster-smoke.sh` exercises the store and the restart path, not the two-JWKS race. Do not read a
single-replica pass as a multi-replica guarantee.

### 6.2 Configuration fail-fast

§3 makes `appsettings.json` ship every credential blank so that a real environment has to supply its
own. Nothing checked that it did. A deployment missing the confidential client secret **booted
cleanly**, passed both health probes, and failed hours later as a 401 from `api/users` in the middle
of somebody's registration — with nothing in the logs pointing at configuration.

`AddRequiredConfiguration` (`Common.Presentation/Security`) is the check. A host declares the
configuration keys it cannot run without; outside Development a blank or absent one is a startup
failure naming the key and the environment. Two hosts use it, and they are the two holding the two
copies of the confidential client secret:

| Host | Required keys |
|---|---|
| Identity | `IdentityServer:IssuerUri`, `Clients:Confidential:ClientId`, `Clients:Confidential:ClientSecret`, `Clients:Public:ClientId` |
| Users | `Duende:AdminUrl`, `Duende:TokenUrl`, `Duende:ConfidentialClientId`, `Duende:ConfidentialClientSecret`, `Duende:Scope` |

Two details worth knowing before a third host adopts it:

- **It is `ValidateOnStart`, invoked one step early.** `ValidateOnStart()` alone defers the check into
  `app.RunAsync()`, which in both hosts is *after* the database migration and the administrator seed.
  Both call `app.Services.GetRequiredService<IStartupValidator>().Validate()` immediately after
  `Build()` instead, so "fail fast" means before any side effect.
- **Repeat calls accumulate.** The keys land in one options instance, so a boot missing three values
  reports all three and costs one restart rather than three.

**A committed fallback defeated all of this and had to go.** `Config.Clients` read the secret as
`configuration["Clients:Confidential:ClientSecret"] ?? "Pzot…"` — the value committed in
`appsettings.Development.json`. Milestone B made `appsettings.json` blank so a real environment must
supply one; that `??` handed it the development secret instead, silently. It is gone: a blank value
now produces a client with **no secret at all**, which fails closed, and outside Development the host
never reaches that line.

### 6.3 Password policy, lockout, token lifetimes

| Setting | Development | Everywhere else | Note |
|---|---|---|---|
| Password length | 1 | **12** (was 8) | The character-class rules — digit, lower, upper, non-alphanumeric — are ASP.NET Identity's defaults and deliberately untouched; only the length moved. Development relaxes everything so the `admin`/`admin` seed works. |
| Lockout | **on** | **on** | 5 failed attempts, 15-minute lock, enabled for new users. |
| Access-token lifetime | 15 min | 15 min | Duende's default is an hour. |
| Refresh token | one-time-only, sliding 8 h, absolute 7 days | same | Claims re-issued on refresh. |
| Client-credentials token | 5 min | 5 min | The Users → Identity provisioning call fetches one per request and caches nothing. |
| Activation-token lifespan | 3 days | 3 days | Unchanged — deliberate, see `registration-architecture-decisions.md`. |

**Why lockout matters more here than the numbers suggest.** `POST /connect/token` does **not** pass
through the Gateway: clients reach Identity directly on `:18080` (compose) or its NodePort (KinD),
the arrangement `deploy/k8s/services/identity.yaml` describes and §5 does not change. So the edge rate
limiter — the thing that partitions anonymous callers by IP and sheds them — never sees a login
attempt at all. Lockout is not defence in depth on top of the limiter; for this endpoint it is the
only layer. Duende's `ResourceOwnerPasswordValidator` calls
`SignInManager.CheckPasswordSignInAsync(..., lockoutOnFailure: true)`, so enabling the options is the
whole of the wiring.

**Why 15 minutes for an access token.** Nothing in this platform can revoke an issued JWT. Permissions
are re-resolved per request (§1) and cached for five minutes, but the token itself is accepted on its
signature alone until it expires — so the default hour is how long a stolen token stays useful.
Fifteen minutes plus a rotating refresh token is the standard trade: a client refreshes four times an
hour instead of once, and each refresh re-checks that the account still exists and is not locked out.
One-time-only refresh tokens make a leaked refresh token *detectable* — using one twice invalidates
the chain — and that rotation state is exactly what needed the persisted-grant store from §6.1.

**A raised password floor has a silent failure attached to it.** `AdminSeeder` logs an error and lets
the host start when the seed password fails the policy, so a cluster gets a perfectly healthy Identity
with no administrator in it and nothing else able to create one. Moving the floor from 8 to 12 is what
forced `deploy/k8s/base/config.yaml`'s `AdminSeed__Password` to grow a character, and
`IdentityHardeningTests` now checks that manifest value against the length it reads out of
`Program.cs`.

### 6.4 Guardrails

| Property | Where it is asserted |
|---|---|
| A required key blank outside Development fails the boot naming the key; Development is exempt; every missing key is reported, not just the first | `Common.UnitTests/Security/RequiredConfigurationTests` |
| Identity registers an operational store and a shared Data Protection ring | `IdentityHardeningTests.Identity_Should_PersistSigningKeysInAStoreEveryReplicaShares` |
| Lockout is configured, and enabled for new users | `IdentityHardeningTests.Identity_Should_EnableLockoutOnTheTokenEndpoint` |
| `Config.cs` does not fall back to the committed development client secret | `IdentityHardeningTests.IdentityConfig_Should_NotFallBackToTheCommittedDevelopmentSecret` |
| The seeded administrator password satisfies the non-Development policy, at the length `Program.cs` actually requires | `IdentityHardeningTests.KubernetesManifest_Should_SeedAnAdministratorPasswordThatSatisfiesThePolicy` |
| No `keys` volume is mounted — which would mean the file-system key store is back | `IdentityHardeningTests.KubernetesManifest_Should_NotMountAKeysVolume` |
| Five failed logins lock an account, proved by the *correct* password failing afterwards | `Users.IntegrationTests/Lockout/AccountLockoutTests` |
| A wrong password and an unknown account are indistinguishable | `AccountLockoutTests.TokenEndpoint_Should_NotRevealWhetherTheAccountExists` |

`AccountLockoutTests` needs `fooddeliveryservice.identity` running on `:18080`, like every other
integration suite; it is excluded from `ci.yml` for that reason.

### 6.5 The JWT role claim — an architectural decision, not an omission

`FoodDelivery_ProjectPlan.md` Feature 3.6 asserts *"RBAC enforced at the API Gateway level (JWT role
claim check)"*. **That is not what this platform does, and it is not going to be.** Stated plainly
here because a reviewer who greps for it and finds nothing concludes the wrong thing.

Identity registers `IdentityRole` but assigns no roles and has no `IProfileService`; the role a user
actually has (`Administrator`, `Customer`, `RestaurantManager`, `Driver`, `SupportAgent`) is a
**Users-module** concept living in the Users database. Minting it into the JWT would mean Identity
reading Users' data — either a cross-service call in the wrong direction, or a second replica of the
role table inside Identity. Both are features; neither is hardening.

**The design is permission-based authorization, resolved at the service.** A token carries the subject
and nothing about what the subject may do. Each service resolves permission codes from Users over
MassTransit RPC, caches them in Redis for five minutes, and enforces them per endpoint (§1). The
Gateway's contribution is coarse and real: it validates the JWT and rejects anything unauthenticated
before it touches a service, and it rate-limits (§5). Defence in depth here is *double validation plus
per-endpoint permission policies*, not two different authorization models.

**The trade-off, stated rather than buried:** the Gateway can shed an *unauthenticated* request, not
an *unauthorized* one. A caller holding a valid token for a permission they do not have is proxied to
the owning service, which loads their permissions and returns 403. That costs one hop and one (usually
cached) permission lookup per rejected request. It buys a single source of truth: permissions change
in Users and take effect within the cache TTL, with no token re-issue and no claim going stale inside
a JWT somebody is still holding.

## 7. What Milestones A, B, C, D and E do not cover

Named so a reader does not mistake this page for the finished document: input validation and the
error surface (Milestone F), API documentation reachability (G), supply-chain scanning (H), and the
consolidated OWASP pass, TLS boundary and known-limitations sections (I).
