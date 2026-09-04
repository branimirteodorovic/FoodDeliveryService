# Feature 3.7 — Final Production Hardening — Implementation Plan

> Eleventh implementation plan, after `RESTAURANTS_PHASE1_PLAN.md`, `ORDERS_PHASE1_PLAN.md`, `NOTIFICATIONS_PHASE1_PLAN.md`, `DELIVERY_PHASE2_PLAN.md`, `REALTIME_PHASE2_PLAN.md`, `CACHING_PHASE2_PLAN.md`, `TELEMETRY_PHASE2_PLAN.md`, `KUBERNETES_PHASE2_PLAN.md`, `LOADTESTING_PHASE3_PLAN.md` and `SUPPORT_PHASE3_PLAN.md`. This one covers **Feature 3.7 — Final Production Hardening** from `FoodDelivery_ProjectPlan.md`.

> **Scope:** a closing pass over the whole platform — nine hosts, seven modules — turning the things that are *currently true because nobody broke them* into things that **cannot** silently stop being true. The through-line of every milestone below is the same: **do not write a security checklist, write a test or a CI gate that fails when the property is violated.** A markdown audit rots in a week; `ObservabilityAssetTests` has been failing builds for a month and is the model this plan copies seven times.

Decisions locked in for this plan:

- **Every audit finding becomes an executable guardrail.** Where a property can be asserted from the endpoint metadata, the config files, the manifests or the OpenAPI document, it is asserted in `FoodDeliveryService.Common.UnitTests` or in a CI job. Where it genuinely cannot be, it is written into `docs/security.md` with the reason it is unenforceable — and that is the exception, not the pattern.
- **This feature adds almost no runtime behaviour.** The platform is already correct on the big things (every endpoint carries `RequireAuthorization`, JWT is validated twice, no service reads another's database, the edge rate limiter sheds load, secrets are blank in `appsettings.json`). What it lacks is *proof*, *documentation*, and a handful of genuinely missing edge concerns: CORS, forwarded headers, security response headers, least-privilege database roles, Duende key management outside Development, and dependency scanning. Milestones D, E and H carry the only real code; the rest is guardrails and docs.
- **The known truthfulness gap is closed, not papered over.** The root `README.md` currently claims "8 hosts", marks **Support** and **order placement idempotency** as `📋 Pending` when both shipped, and marks this feature pending. A README that lies about what exists is worse for a portfolio than one that omits things. Milestone I is a correctness pass over it, not a decoration pass.
- **Nothing here reopens a scoped-out decision.** `KUBERNETES_PHASE2_PLAN.md` was deliberately cut short by the user (no Helm/HPA/Ingress/AKS). TLS therefore terminates *outside* anything this repo deploys, and this plan hardens for that reality rather than pretending an Ingress exists. Likewise the "final Azure cost review" in the project plan has no Azure subscription behind it — Milestone I delivers it as a documented sizing/cost model, and says so plainly.
- Reference implementations to mirror: **`ObservabilityAssetTests`** for asset-validating tests, **`deploy/k8s/scripts/policy-check.py`** for manifest policy, **`docs/rate-limiting.md`** and **`docs/caching.md`** for the documentation shape, **`Common.Presentation/RateLimiting`** for a cross-cutting middleware that ships with its own options + tests.

---

## 0. Prerequisites — what already exists, and what does not

**Already in place — do not rebuild:**

| Thing | Where | Note |
|---|---|---|
| Authorization on every endpoint | `Modules/*/…Presentation/**` | Every `IEndpoint` calls `.RequireAuthorization(Permissions.X)`. The only `AllowAnonymous` are `users/register`, `users/accept-invitation` and the three health probes. Milestone A locks this in; it does not have to fix it. |
| Double JWT validation | Gateway + every module host | Gateway rejects unauthenticated traffic; services re-validate and resolve permissions via `IPermissionService`. |
| Blank secrets in `appsettings.json` | all nine hosts | Real values live only in `appsettings.Development.json`, which is valid against a throwaway local stack. |
| Edge rate limiting | `Common.Presentation/RateLimiting` + Gateway | Redis-backed, route-tiered, `docs/rate-limiting.md`. OWASP A04-adjacent work is already done. |
| Health probes, correlation, telemetry | all nine hosts | `docs/health-probe-contract.md`, `docs/observability-backend.md`. |
| NuGet audit as an error | `Directory.Build.props` (`TreatWarningsAsErrors`) | A vulnerable transitive already breaks the build — that is how the SSH.NET/Testcontainers bump happened. Milestone H adds the *scheduled* half, not the build-time half. |
| Kubernetes pod security policy | `deploy/k8s/scripts/policy-check.py` | Already checks images, resources, probes, env and pod security context. Milestone B extends it; it does not start it. |
| Architecture diagrams | root `README.md` | C1/C2 Mermaid diagrams exist and are current. Milestone I adds C3-level message-flow and corrects the stale prose around them. |

**Explicitly NOT available, and this plan does not pretend otherwise:**

- **No role claim is minted in the JWT.** `FoodDeliveryService.Identity` registers `IdentityRole` but assigns no roles and has no `IProfileService`; the module-side `Role` lives only in the Users database and reaches services through `GetUserPermissionsRequest`. The project plan's "RBAC enforced at the API Gateway level (JWT role claim check)" is still not implementable — see `SUPPORT_PHASE3_PLAN.md` §0 and its Milestone I. §6.5 below decides what to do about it.
- **No TLS anywhere in this repo.** `docker-compose` and the KinD manifests are HTTP-only by design (`ASPNETCORE_HTTP_PORTS: "8080"`, no certificate in any pod). "All endpoints use HTTPS" is a deployment-boundary claim, not a code claim, and Milestone D hardens the *code* for living behind a TLS-terminating proxy rather than adding a certificate nobody has.
- **No Azure subscription.** Nothing in this plan provisions, measures or bills an Azure resource. Milestone I's cost section is a sizing model derived from the measured load-test numbers in `docs/load-testing.md`, and is labelled as such.
- **No AI features exist** (3.1–3.3), and **FraudDetection was reverted** (`6ae4879`). Documentation must not describe either as present. Stale `bin/obj` output for `FoodDeliveryService.FraudDetection.Api` is still on disk and tracked by nothing — Milestone B's scanner must not trip over it, and Milestone I must not list it as a service.

---

## 1. Architecture overview

Nothing in this feature changes the topology. The surfaces it touches:

| Surface | Milestone | What changes |
|---|---|---|
| `Common.UnitTests` | A, F, G | Three new asset-validating test classes: endpoint authorization coverage, validator coverage, OpenAPI completeness. |
| `Common.Presentation` | D | New `SecurityHeaders` middleware + `Cors` extension, both shared, both used by all nine hosts through one call each. |
| `Common.Infrastructure` | E | Startup configuration validation (fail fast on a blank secret outside Development). |
| Gateway | D, G | Security headers, forwarded headers, CORS policy, aggregated API documentation. |
| Identity | E | Signing-key management outside Development, token lifetimes, lockout, non-dev password policy. |
| `docker/`, `deploy/k8s/` | C, B | Per-service least-privilege Postgres roles; a secrets policy check. |
| `.github/workflows/` | B, H | `gitleaks`, `dependency-review`, CodeQL, Trivy, SBOM, Dependabot config. |
| `docs/`, `README.md` | I | `docs/security.md`, `docs/api-documentation.md`, `docs/cost-model.md`, README correctness pass. |

**Milestone order matters in exactly two places:** A must land before I (the README should not claim a guarantee before the test that proves it exists), and C must land before I (the cost model references the connection-pool sizing C changes). Everything else is independent and can be built in any order.

---

## 2. Milestone A — The security audit, as tests

**PR size: medium.** One new test folder in `Common.UnitTests`, one CI list edit, one short doc.

The project plan's first task is *"ensure all endpoints validate JWT tokens"*. They do. This milestone makes that a build failure the day one does not.

### 2.1 `Security/EndpointAuthorizationTests.cs` (`Common.UnitTests`)
Reflect over each module's `Presentation.AssemblyReference.Assembly`, build a real `WebApplication` route table via `AddEndpoints` + `MapEndpoints` against a stub `IServiceCollection`, then walk `EndpointDataSource.Endpoints` and assert, per endpoint:

- It carries either an `IAuthorizeData` metadata item **or** `IAllowAnonymous`, never neither.
- Every `IAllowAnonymous` endpoint is on an **explicit allow-list** declared in the test file — today `users/register`, `users/accept-invitation`, `/health`, `/health/live`, `/health/ready`. A new anonymous endpoint fails the build until someone adds it to the list *in the same PR*, which is the review prompt this milestone exists to create.
- Every authorized endpoint's policy name resolves to a real `Permission` code. A typo'd policy string today produces a 403 at runtime and nothing at build time.

> **Gotcha to expect:** the module `Presentation` assemblies are `internal`-heavy and their endpoints are `internal sealed`. `AddEndpoints` already discovers them by reflection, so the test must go through that path rather than scanning for public types — a naive `assembly.GetExportedTypes()` finds nothing and the test passes vacuously. Assert a non-zero endpoint count per module first, for exactly that reason.

### 2.2 `Security/GatewayRouteTests.cs`
Parse `src/API/FoodDeliveryService.Gateway/appsettings.json` + `appsettings.Development.json` and assert:

- Every route names a cluster that is **defined** (this is the class of bug `KUBERNETES_PHASE2_PLAN.md` A0 found breaking `users/register`).
- Every route carries an `AuthorizationPolicy`, and the only routes with `"anonymous"` are the two registration paths.
- Every module path prefix in the platform (`orders/**`, `restaurants/**`, `users/**`, `notifications/**`, `delivery/**`, `support/**`, `hubs/**`) has a route. A service reachable only by its container port is a service outside the gateway's auth and rate limiting — hard rule #10, asserted.

### 2.3 Ownership / IDOR review (documented, partly asserted)
Walk every read endpoint that takes an id and confirm the handler scopes by the caller. The established convention is **404, not 403, when the resource is not the caller's** (do not leak existence) — `SUPPORT_PHASE3_PLAN.md` §2.2 states it, and Orders/Delivery follow it. Record the sweep as a table in `docs/security.md` (§9.1); where a handler is found scoping by nothing, fix it in this PR and add an integration test in the owning module's suite.

### 2.4 CI
Add nothing new to the workflow: `Common.UnitTests` is already the first entry in `ci.yml`'s hardcoded `projects` list, so these tests run on every PR the moment they exist.

### 2.5 What Milestone A shipped, and what it found

`Common.UnitTests/Security/EndpointAuthorizationTests.cs` (six tests, five of them per-module theories) and `Common.UnitTests/Security/GatewayRouteTests.cs` (six tests, most of them a theory over the two routing copies), plus `Common.UnitTests/RepositoryPaths.cs` — the `BackendPath` walk-up `ObservabilityAssetTests` had private, now shared. Suite goes 176 → 204 green.

**Departures from §2.1–§2.3, and the reasons:**

- **`Common.UnitTests` now references all seven module `Presentation` projects and `Users.Domain`.** There is no way around it: the endpoints are `internal sealed`, so only `AddEndpoints`' reflection reaches them, and reflection needs the assemblies loaded. The csproj carries a comment saying these are the project's only module references and that nothing in Common's *product* code may acquire one. `Users.Domain` comes for `Permission`, which is the seeded list the policy names are checked against.
- **The route table cannot be read without registering the services endpoint delegates inject.** Minimal API infers any handler parameter it cannot find in DI as the request *body*, and a route with an inferred body next to a route value throws from `EndpointDataSource.Endpoints` — so the table is unreadable, not merely unannotated. Two registrations fix it (`ISender`, `IDateTimeProvider`) and both are factories that throw, because metadata inference only asks whether the type *is* a service and never resolves one. **A new endpoint injecting a new service fails these tests with `Body was inferred but the method does not allow inferred body parameters`; the fix is one more `RegisterInjectedService<T>` line, not a change to the endpoint.**
- **`WebApplication.CreateSlimBuilder()`, and `AddSignalR()` before `MapEndpoints()`.** RealTime's `TrackingHubEndpoint` calls `MapHub`, which throws at *map* time without the SignalR services — the only dependency an endpoint's mapping has here. `MapHub` also contributes **two** endpoints (`hubs/tracking` and `hubs/tracking/negotiate`), both of which land in the "authenticated, no permission policy" allow-list.
- **Two allow-lists, not one.** §2.1 asks for the anonymous list; the hub needed a second one for endpoints authorized as "any authenticated principal" (`RequireAuthorization()` with no argument). That is a real distinction — an empty policy name never reaches `PermissionAuthorizationPolicyProvider` — and leaving it unlisted would have meant either failing the hub or silently accepting every unpolicied endpoint.
- **Health probes are asserted separately.** They are mapped by `MapHealthProbes`, not discovered as `IEndpoint`s, so they never appear in a module's route table. One `[Fact]` maps them on their own and asserts those three paths and no others are anonymous.
- **§2.2's gateway assertions were extended to the Kubernetes copy.** `deploy/k8s/services/gateway.yaml` carries a hand-maintained duplicate of the whole routing table as `appsettings.Kubernetes.json`, with a comment asking the next author to edit both places. Every §2.2 assertion runs over both, and one more compares them for drift. The manifest is parsed as YAML (`YamlStream`) rather than sliced out of the text, so a renamed ConfigMap fails rather than matching nothing.
- **`ModulePermissionConstants_Should_MatchASeededPermissionCode` is an addition.** Each module's `Permissions` class claims in a comment to mirror the Users rows; nothing checked it, and an unused-but-wrong constant is the one that gets copied onto the next endpoint.

**§2.3's sweep found no handler scoping by nothing** — every endpoint taking an id either checks ownership, filters by it in SQL, or is deliberately open to all authenticated callers (the restaurant catalogue). It did find that **§2.3's premise is wrong**: Orders and Delivery did *not* follow the 404-not-403 convention, only Support did. `GET orders/{id}`, `GET delivery/deliveries/{id}` and `GET delivery/orders/{orderId}/delivery` checked ownership *after* loading the row and returned `Error.Problem` → **HTTP 400**, which tells a caller that a guessed id is real.

**Fixed in this milestone, on the user's instruction**, the way §2.3 and Support's precedent describe — the predicate moved into the `WHERE` clause rather than the error type being swapped, so the 404 is unfakeable instead of merely conventional:

- Orders inlines `AND (@IsAdmin OR o.customer_id = @UserId OR r.manager_user_id = @UserId)`; the `CanRead` branch and the now-unused `ManagerUserId` projection are gone.
- Delivery's `DeliveryAccess` changed shape from `EnsureCanView(...)` returning a `Result` to a `VisibleToCallerSql` predicate constant plus `CanViewAnyDelivery(context)`, used by both reads so they cannot drift onto different definitions of "yours". `DeliveryErrors.NotAuthorizedToView` was deleted with its last caller.
- `Delivery.IntegrationTests/PickupDeliveryTests` asserts 404 for the bystanding driver (was 400). **Orders gets no equivalent test**: its integration fixture has a single seeded principal, an Administrator, who bypasses ownership by definition — a non-owner read is not expressible there without a second real Duende identity in the fixture. Worth doing if Milestone F touches Orders' error surface.
- Verified for real: Orders 31/31 and Delivery 43/43 integration tests green against the KinD Identity on `:18080` (both suites are excluded from `ci.yml` and need it running).

**Left standing, and named so §7.4 picks it up:** the *write* paths still answer 400 on an ownership failure — `OrderOwnership.EnsureCanManage`, `RestaurantOwnership.EnsureCanModify` and `CancelOrder`'s customer check all return `Error.Problem` with a `NotOwner`/`NotManager` code. Same class of leak, a dozen endpoints across two shared guards, and it belongs with Milestone F's error-surface pass rather than with a read fix. `docs/security.md` §2.3.

`docs/security.md` exists now, holding the authorization model, the guardrail table and the sweep. Milestone I extends that file rather than creating it (§10.3).

---

## 3. Milestone B — Secret hygiene: scanning, and the two committed values

**PR size: small.** One workflow job, one policy-check extension, one config change, one doc section.

The project plan asks for *"no secrets committed to Git"*. The repo is close to compliant already: `appsettings.json` ships blank, only `appsettings.Development.json` carries values. Two of those values still deserve attention.

### 3.1 The duplicated client secret
`PzotcrvZRF9BHCKcUxdKfHWlIPECG49k` is written **twice** — `Identity/appsettings.Development.json` (`Clients:…:ClientSecret`) and `Users.Api/appsettings.Development.json` (`ConfidentialClientSecret`). Two files holding one secret is a drift trap: change one and user registration fails with a 401 from the Identity local API, which surfaces three layers away as "registration is broken". Options, in preference order:

1. **Keep the duplication, add the guardrail** (recommended): a unit test that reads both files and asserts the two values are equal, with a comment naming the failure mode. Cheapest, zero runtime change, and the failure message *is* the documentation.
2. Hoist it to a single `.env` file consumed by compose. Rejected: it splits dev config across two mechanisms and breaks `dotnet run` outside compose.

> **It is written nine times, not twice.** Besides the two host configurations: Identity's `??` fallback literal in `Config.cs`, the Kubernetes `platform-secrets` Secret, and a `const ConfidentialClientSecret` in each of the five integration-test fixtures that mint real Duende tokens (`Delivery`, `Orders`, `RealTime`, `Restaurants`, `Support`). A test comparing *two named files*, as option 1 describes it, would have missed seven copies and passed.
>
> **Neither option shipped: the duplication is documented, not guarded.** The discovery test was built first — it found occurrences by pattern across `.json`/`.cs`/`.yaml`, asserted the distinct set had one element, and asserted a floor of eight matches so a pattern that stopped matching would fail rather than pass vacuously. It worked, and it was then removed as disproportionate: ~150 lines of repository-scanning guarding a value whose worst case is a confusing 401 on a developer machine. The nine locations are tabulated in `docs/security.md` §3.2 with the failure mode instead. **Revisit if a shared credential ever authenticates to something real** — the mechanism was sound, only the ratio was wrong.
>
> Two pattern gotchas from building it, worth keeping if it is ever rebuilt: the value must be matched **quoted**, or `string confidentialClientSecret =\n configuration[…]` matches and captures the word `configuration`; and the JSON, C# and YAML spellings differ enough (`": "`, `= "`, `"] ?? "`, `: ` unquoted) that YAML needs its own expression rather than one clever alternation.

### 3.2 `gitleaks` as a CI gate
Add a `secrets` job to the `tools` workflow running `gitleaks` over the working tree (not the full history — the dev values are legitimately there and rewriting history for a throwaway Postgres password is theatre). Ship a `.gitleaks.toml` that **allow-lists the known development values by path**, so the scan is meaningful rather than permanently red:

```toml
[[rules.allowlist]]
paths = ['''appsettings\.Development\.json$''', '''deploy/k8s/base/config\.yaml$''']
description = "Local-only credentials, valid against a throwaway compose/KinD stack only."
```

Everything outside those two paths is a genuine finding. Point the job at the tree so the stale untracked `bin/obj` output (including the reverted FraudDetection host) cannot produce phantom hits — `git ls-files` is the input, not the filesystem.

> **Shipped with three departures**, all forced by running the scanner rather than reasoning about it. A baseline scan (`ghcr.io/gitleaks/gitleaks:v8.24.0`, default rules) reports exactly **eight** findings, all of them the same client secret, all `generic-api-key`.
>
> 1. **A path-only allowlist leaves five findings red.** Five of the eight are the integration-test fixtures, which are not `appsettings.Development.json`. The allowlist therefore names the two paths **and** the secret **by value** (`regexes`), which is the more accurate statement anyway — *this specific throwaway string is known*, while every other high-entropy value in those same files still fails. Verified both directions: a fresh 32-character secret in a `.cs` file fails the scan, the same value in an allow-listed dev config passes.
> 2. **Global `[allowlist]`, not `[[rules.allowlist]]`.** The sketch's form attaches an allowlist to a rule block that does not exist here (the rules come from `[extend] useDefault = true`). The config uses the top-level `[allowlist]` with `targets = ["file"]`.
> 3. **`git archive HEAD`, not `git ls-files`.** `gitleaks dir` takes a directory, not a file list, so the scan input is built by exporting `HEAD` into a temp directory — same effect, one command, and it is the committed tree by definition. That lives in `Backend/tools/secret-scan.sh` so a developer runs locally exactly what CI runs; CI's `secrets` job is one `bash` line. It is a **separate job**, not a step in `tools`, so it fails independently of actionlint/kubeconform.
>
> A dead allowlist entry is a hazard in its own right — it reads as coverage in review while suppressing nothing — and **nothing checks for one**. A test asserting the config exists, still extends the default rules, and that every `paths` pattern matches a real file was written and then dropped with the §3.1 discovery test, for the same proportionality reason. Note the residual gap honestly: a stale path entry would **not** surface on the next scan either, because the only value the default rules actually detect inside those two files is the client secret, which is separately allow-listed by value — the Postgres and RabbitMQ credentials there are too low-entropy to trip `generic-api-key`. If either allow-listed file is renamed, nothing says so.

### 3.3 Manifest secret policy
Extend `deploy/k8s/scripts/policy-check.py` with one check: **no key whose name matches `(?i)password|secret|key|token|connectionstring` may appear in a `ConfigMap`'s `data`** — such keys belong in the `Secret`. `config.yaml` already respects this; the check keeps the next Deployment from quietly regressing it. Fast, no cluster, and it runs in the existing `tools` job.

> **That regex fails on the current manifests as written.** `platform-config` legitimately carries `Authentication__TokenValidationParameters__ValidIssuers__0` and `…__1`, and `token` matches. The rule shipped with a `CONFIGMAP_KEY_EXEMPTIONS` list — pattern plus the reason it is not a credential — mirroring the anonymous-endpoint allow-list in Milestone A: the exemption is the review prompt. Two neighbours to expect when the list is next touched: `RateLimiting:KeyPrefix` (a Redis key namespace) trips `key`, and the same exemption exists in `SecretHygieneTests` for the `appsettings.json` sweep.
>
> The rule was also **extended to embedded JSON**. Two of the three ConfigMaps hold a whole file under one innocuous key (`appsettings.Kubernetes.json`, `redis.conf`), so a key-name check over `data` alone would never see a connection string pasted into the Gateway's routing table — the one place that could realistically happen. Values whose key ends `.json` are parsed and walked, and a value that claims to be JSON but does not parse is itself a failure. `check_document` now dispatches on `ConfigMap` before the workload kinds, and the summary line counts both.

### 3.4 Documentation
`docs/security.md` § "Secrets": what is committed and why it is safe, what a real environment must supply (the exact key names — they do not change between local and Azure, which is the whole point of the ConfigMap/Secret split), and the Azure Key Vault mapping table (`platform-secrets` key → Key Vault secret name).

> Shipped as §3 of `docs/security.md` (§3.1 what is committed, §3.2 the nine copies, §3.3 the three gates and the two scoping decisions, §3.4 the Key Vault mapping, §3.5 known limitations). Two things the sketch did not anticipate: Key Vault names cannot contain `_`, so every `__` becomes `--` in that column; and Identity's missing signing-key store is a **secrets** limitation, listed in §3.5 with a pointer to Milestone E so the section is not read as complete.
>
> One free assertion came out of writing §3.1: all nine hosts' base `appsettings.json` ship every credential-shaped key blank, which is the claim §0 of this plan makes and nothing checked. `SecretHygieneTests.BaseAppSettings_ShipsEveryCredentialBlank` is a `[Theory]` over the host directories, so a tenth host is covered without editing the test.

**Shipped:** `.gitleaks.toml`, `Backend/tools/secret-scan.sh`, the `secrets` job in `.github/workflows/ci.yml`, the ConfigMap rule in `policy-check.py`, `Common.UnitTests/Security/SecretHygieneTests.cs` (a 9-case `[Theory]`, one per host), and `docs/security.md` §3. No runtime code changed. Policy check passes over 12 workloads and 3 ConfigMaps; the secret scan is clean over 1,380 tracked files.

**Deliberately not shipped**, after the user asked for the milestone to be trimmed: the two repository-scanning tests described in §3.1 and §3.2. Both worked; both were disproportionate to a credential that is only valid against a local stack, and the scanning machinery they needed (~150 lines walking the tree with regexes) reads as gold-plating rather than judgment in a portfolio repository. The properties they asserted are stated in `docs/security.md` §3.2 instead, where the residual gap is named rather than implied. **The same proportionality question applies to the milestones below** — C, D and E carry real runtime substance; F–I should be weighed against this one before being built as written.

---

## 4. Milestone C — Least-privilege database accounts

**PR size: medium.** One SQL init script, compose + manifest wiring, connection-string changes across nine hosts, one doc section.

Today every service connects as the Postgres **superuser** `postgres`, and one server holds all eight databases. That means a SQL-injection or a deserialization bug in *any* service is a full-platform compromise, and hard rule #5 ("never query another service's tables") is enforced by convention alone. This is the single most substantive security change in the feature.

### 4.1 Per-service roles
A `docker/postgres/init/01-roles.sql` mounted into the Postgres container's `docker-entrypoint-initdb.d`, creating for each of the eight databases:

- `fds_{service}_owner` — owns the schema, holds DDL rights. Used **only** by the startup migration path (`app.ApplyMigrations()` / `dbContext.Database.MigrateAsync()`).
- `fds_{service}_app` — `CONNECT` on its own database only, plus `SELECT, INSERT, UPDATE, DELETE` on `public` (with `ALTER DEFAULT PRIVILEGES` so tables created by a later migration are covered). **No** `CREATE`, no access to any other database.
- `REVOKE CONNECT ON DATABASE … FROM PUBLIC` on every database, which is what actually stops `fds_orders_app` from opening `fooddeliveryservice_users`.

### 4.2 The migration-runner problem, and the decision
Migrations run **at startup, in-process**, from the same host that then serves traffic (`ApplyMigrations`). A single connection string therefore cannot be both DDL-capable at boot and DML-only afterwards. Three options:

1. **Two connection strings per host** — `ConnectionStrings:DatabaseMigrations` (owner) used only by the `ApplyMigrations` scope, `ConnectionStrings:Database` (app) used by the `DbContext` and by Dapper's `IDbConnectionFactory`. Recommended: it is ~10 lines in `AddInfrastructure`, keeps the boot-time schema bootstrap that every other plan depends on, and the privileged credential is never held by a request-serving connection pool.
2. Move migrations to an init container / job. Correct for production, but it is a Kubernetes-shaped change into a workstream the user scoped out.
3. Leave the owner in place everywhere. Rejected — it is the status quo with extra files.

Take option 1, and **write the departure into this section if the build proves otherwise**: EF Core's design-time factories (`dotnet ef migrations add`) also read `ConnectionStrings:Database`, so the tooling path needs the owner string too — expect to point the design-time factory at `DatabaseMigrations` and expect that to be the fiddly part of the PR.

### 4.3 Pool sizing stays put
`Maximum Pool Size=10` per host (20 for Identity) and `max_connections` on the server were tuned in `LOADTESTING_PHASE3_PLAN.md` Milestone F against a measured `53300: too many clients` failure. Splitting into two credentials adds a second, near-idle pool per host — keep the migration pool tiny (`Maximum Pool Size=2`) and re-check the arithmetic against the server's `max_connections` in the same PR. Do not silently change the tuned numbers.

### 4.4 Tests
- Integration (one suite is enough — `Orders.IntegrationTests`): connect as the app role and assert that `CREATE TABLE` fails and that connecting to `fooddeliveryservice_users` fails. Testcontainers gives a real Postgres, so this is a real assertion rather than a mock.
- The KinD `cluster-smoke.sh` already exercises boot-time migrations for all nine hosts; if the two-credential split is wrong, that job goes red, which is the coverage that matters.

---

## 5. Milestone D — Edge hardening: headers, forwarded headers, CORS

**PR size: medium.** One new shared middleware + options, one CORS extension, nine one-line host edits, tests.

### 5.1 `SecurityHeaders` (`Common.Presentation/Security`)
One `app.UseSecurityHeaders()` on all nine hosts, mirroring how `UseRequestCorrelation()` replaced seven hand-rolled copies. Sets, on every response:

| Header | Value | Why |
|---|---|---|
| `X-Content-Type-Options` | `nosniff` | Free; stops MIME confusion on the JSON surface. |
| `X-Frame-Options` | `DENY` | The API serves no framable UI. |
| `Referrer-Policy` | `no-referrer` | |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'` | An API returning JSON needs nothing. **Except** the Swagger UI route, which needs `script-src`/`style-src 'self' 'unsafe-inline'` — carve it out by path or the docs page renders blank, and that is precisely the kind of self-inflicted breakage this milestone must not cause. |
| `Strict-Transport-Security` | set **only** when the request arrived over HTTPS | Emitting HSTS over plain HTTP is meaningless, and pinning a browser to HTTPS against a stack that has no certificate makes the local platform unusable. |

Also remove the `Server` header (Kestrel's `AddServerHeader = false`).

### 5.2 Forwarded headers — and a real bug it fixes
No host configures `UseForwardedHeaders`. Two consequences, one of which is live today:

- **The edge rate limiter partitions anonymous callers by IP** (`docs/rate-limiting.md`). Behind any TLS-terminating proxy or ingress, `HttpContext.Connection.RemoteIpAddress` is *the proxy*, so every anonymous request on the planet shares one bucket — the limiter degrades from per-client to global the moment anything sits in front of the Gateway. This is not hypothetical for a deployed system; it is the intended deployment.
- Serilog request logs and traces record the proxy's address as the client's.

Add `ForwardedHeaders` (`XForwardedFor | XForwardedProto`) on the **Gateway only**, before `UseRequestCorrelation()`, with `KnownNetworks`/`KnownProxies` configured from appsettings and **empty by default** — an unrestricted `X-Forwarded-For` is a client-controlled spoof of the rate-limit partition key, which is worse than the bug it fixes. Module hosts sit behind YARP on a private network and keep the default. Write the trade-off into `docs/rate-limiting.md` § the partition-key section, cross-referencing here.

### 5.3 CORS
The Angular SPA (`Frontend/FRONTEND_PLAN.md`) names CORS as a backend prerequisite and nothing provides it. Add it **at the Gateway only** — it is the single public entry point, and a per-service policy would be seven places to drift. A named policy with origins bound from configuration (`Cors:AllowedOrigins`), `AllowCredentials` (SignalR needs it), and the `X-Correlation-Id` + `Retry-After` response headers exposed so the SPA can surface both. Never `AllowAnyOrigin` together with `AllowCredentials` — the framework throws at startup, which is the one place this can go wrong loudly instead of quietly.

`hubs/**` needs the policy applied to the proxied SignalR route specifically; verify with the existing `RealTime.IntegrationTests` client rather than by eye.

### 5.4 Tests
`Common.UnitTests/Security/SecurityHeadersTests.cs` — headers present on a 200, on a 404, and on the `ApiResults.Problem` path (a middleware that only decorates success responses is a common miss); HSTS absent over HTTP and present over HTTPS; the Swagger carve-out returns a CSP that actually permits the UI.

---

## 6. Milestone E — Identity hardening

**PR size: medium.** Identity host + config, one migration if lockout state needs persisting, tests.

`FoodDeliveryService.Identity` is the platform's most security-sensitive host and has had the least hardening attention.

### 6.1 Signing key management outside Development
`Program.cs` calls neither `AddDeveloperSigningCredential` nor `AddSigningCredential`. Duende 8 falls back to **automatic key management**, which needs a store to persist keys into; with only the ASP.NET Identity `DbContext` registered and no operational store, key material is not durably persisted — so a restart, or a second replica, can serve tokens signed by a key the *other* replica's JWKS does not advertise. Every service validating against the discovery document then rejects perfectly good tokens, intermittently. That is a nasty, load-balancer-only failure and exactly the class of thing "production hardening" means.

Decide and implement one of:
1. Point Duende's automatic key management at a persisted store (its `Duende.IdentityServer.EntityFramework` operational store, or the file-system store for a single-node deployment).
2. Configure an explicit `AddSigningCredential` from a certificate supplied through configuration outside Development.

Whichever is taken, `ASPNETCORE_ENVIRONMENT: Kubernetes` (the manifests' value) must exercise it — the `cluster-smoke.sh` job is the proof, and the RealTime host is already pinned to one replica for unrelated reasons, so do not mistake a single-replica pass for a multi-replica guarantee. State the choice and the residual limitation here when built.

### 6.2 Configuration fail-fast
`appsettings.json` ships `ClientSecret: ""` and `AdminSeed.Password: ""` so that real environments must supply their own — but nothing *checks*. A misconfigured deployment currently starts happily with an empty client secret. Add options validation (`ValidateOnStart`) in Identity and Users: outside Development, a blank `ClientSecret` / `ConfidentialClientSecret` is a startup failure with a message naming the configuration key. Fail-fast at boot beats a 401 in production three hours later.

### 6.3 Password policy, lockout, token lifetimes
- The Development branch relaxes password rules to length 1 for the seeded `admin/admin`; the non-Development branch sets only `RequiredLength = 8`. Bring the non-Development branch to a defensible baseline (length 12, and rely on ASP.NET Identity's defaults for the character classes rather than weakening them).
- **Enable lockout** — `MaxFailedAccessAttempts` + `DefaultLockoutTimeSpan`, and confirm the password sign-in path passes `lockoutOnFailure: true`. Without it the token endpoint is an unrated password oracle; the Gateway's rate limiter partitions by IP for anonymous callers, which slows a single-source attack and does nothing about a distributed one.
- Review access-token lifetime (Duende default 1 h) and refresh-token handling against the SPA's needs; document the chosen numbers in `docs/security.md`.
- Keep the 3-day invitation token lifespan — it is deliberate and documented.

### 6.4 Tests
`Users.IntegrationTests` already runs real Duende flows. Add: a wrong-password login attempt repeated past the threshold returns a lockout failure rather than an indefinite retry, and an activation token past its lifespan is rejected.

### 6.5 The JWT role claim — decision
`FoodDelivery_ProjectPlan.md` Feature 3.6 asserts gateway-level RBAC by role claim. It is still not implementable (§0). Two honest resolutions:

1. **Implement it** — an `IProfileService` in Identity that mints role claims, requiring Identity to know the module-side `Role` that lives in the Users database. That is a cross-service read in the wrong direction, or a replica in Identity, and it is a *feature*, not hardening.
2. **Document the actual design** — authorization is permission-based, resolved from Users over MassTransit RPC and cached, enforced at the service and coarsely at the Gateway by "authenticated or not". Defence-in-depth is achieved by double JWT validation plus per-endpoint permission policies.

**Take (2)** and write it up in `docs/security.md` as an explicit architectural decision with its trade-off (the Gateway cannot shed an unauthorized request before proxying it). Do not leave the project plan's claim standing unqualified — a reviewer who greps for it and finds nothing concludes the wrong thing.

---

## 7. Milestone F — Input validation & the error surface

**PR size: small–medium.** One coverage test, bounded-pagination fixes, error-shape assertions.

*"All user inputs are sanitised"* is the project plan's phrasing; the accurate framing for this codebase is **validated at the boundary, parameterised at the database, and never echoed raw in an error**.

### 7.1 Validator coverage test (`Common.UnitTests/Security/ValidatorCoverageTests.cs`)
Reflect over every `ICommand<T>` / `IQuery<T>` in every module's Application assembly and assert an `AbstractValidator<T>` exists — with a declared, commented exception list for the genuinely field-free requests (`GetMyDriverProfile`, etc.). `ValidationBehavior` silently no-ops for a request with no validator, so a missing one is invisible today.

### 7.2 Bounded inputs
Sweep every paginated query for an unbounded `pageSize` (a `pageSize=1000000` against the restaurant search is a free denial of service that the rate limiter counts as one request), every free-text field for a maximum length, and every `decimal`/`int` for a range. Fix in place; each fix gets a unit test in the owning module's suite.

### 7.3 Injection posture, asserted where it can be
Reads are Dapper with parameters, writes are EF Core — both parameterised. Add a test that greps the Application assemblies' source for string-interpolated SQL (`$"SELECT`, `+ " WHERE"`), the same shape as `ObservabilityAssetTests`' file scanning. It is a crude check and it will catch the exact regression it exists for.

### 7.4 Error surface
Assert that `ApiResults.Problem` never emits a stack trace or an inner exception message outside Development, and that `Include Error Detail=true` (present in every Development connection string) is absent from the non-Development configuration — an Npgsql error detail can carry row data into a client response.

---

## 8. Milestone G — API documentation

**PR size: medium.** Per-host OpenAPI enrichment, Scalar UI, gateway aggregation, one completeness test, one doc.

Today: `AddOpenApi()` + `AddSwaggerGen()` with a title and nothing else; UI mapped **only** in Development; the Gateway serves no documentation at all. So the documented API surface is invisible from the one place the whole API is reachable.

### 8.1 Enrich the document
The seven module hosts share one `SwaggerExtensions.cs` copy each — hoist it into `Common.Presentation` as `AddApiDocumentation(serviceName)` (the seventh copy is one too many, exactly as with correlation). It should add:

- Per-service title/description/contact, not the shared "modular monolith" string, which is doubly wrong: these are microservices, and it is identical across seven services.
- The **Bearer security scheme**, with the permission policy of each endpoint surfaced in its description. Reviewers open Swagger to learn what a call requires; today it says nothing about auth.
- Response schemas for the failure paths — `ProblemDetails` for 400/403/404/409 and 429 — via the endpoint conventions, so `.Produces` does not have to be written on 60 endpoints by hand.
- `WithSummary`/`WithDescription` on every endpoint, which is where the actual writing work is.

### 8.2 Scalar
The project plan names "Swagger / Scalar". Add `Scalar.AspNetCore` alongside Swashbuckle, served at `/scalar/v1`. Keep both — Swagger UI is the familiar one, Scalar is the presentable one.

### 8.3 Reachability
Map the documentation UI outside Development too, but **behind the same authorization as the rest of the surface** in non-Development environments (an unauthenticated schema dump of every endpoint is free reconnaissance). Then add a gateway route so `GET /docs/{service}` proxies to that service's UI — a reviewer with one URL can then see all seven APIs. Note the CSP carve-out from §5.1 applies to these paths.

### 8.4 Completeness test
`Common.UnitTests/Documentation/OpenApiDocumentTests.cs` — build each host's OpenAPI document in-memory and assert every operation has a summary, every non-anonymous operation declares the security requirement, and every operation declares at least its success and its 400/401 responses. This is the guardrail that keeps §8.1's writing from decaying the moment the next endpoint is added.

### 8.5 `docs/api-documentation.md`
Where each service's docs live, how to get a token to try a call (the ROPC flow the Frontend plan uses), and the tags convention.

---

## 9. Milestone H — Supply chain: Dependabot, CodeQL, image and SBOM scanning

**PR size: small.** Config files and workflow jobs; no application code.

### 9.1 Dependabot
`.github/dependabot.yml` with three ecosystems: `nuget` (rooted at `Backend/`, grouped so the OpenTelemetry / MassTransit / EF Core families move together rather than as fifteen PRs), `github-actions`, and `docker` (the nine Dockerfiles + `docker-compose.yml` images). Weekly, with an open-PR cap. Central package management means every bump is a single `Directory.Packages.props` edit — Dependabot handles this correctly and the resulting PRs are one-line and reviewable.

Note in the file *why* the grouping exists: `TreatWarningsAsErrors` plus `AnalysisMode=All` means a bump that introduces one new analyzer warning fails the whole build, so ungrouped bumps produce a queue of independently red PRs.

### 9.2 `dependency-review` + CodeQL
- `actions/dependency-review-action` on pull requests: fails a PR that *introduces* a vulnerable or badly-licensed dependency. Complements the build-time NuGet audit by naming the offending PR rather than the branch.
- CodeQL for `csharp` on PR + a weekly schedule. It is the standard SAST for a public .NET repo and its findings land in the Security tab.

### 9.3 Container image scanning + SBOM
A `trivy` job scanning the built images for OS and library CVEs — start it as **warn-only** (`exit-code: 0`) and promote to blocking once a clean baseline is established; a hardening PR that lands permanently-red CI is worse than no scan. Generate a CycloneDX SBOM per image and upload it as a workflow artifact.

Reuse the existing image build rather than adding a second one: the `cluster` job already builds all nine images through `kind-up.sh`, so scanning belongs either in that job or in a small dedicated build. Do not double the CI's most expensive step.

### 9.4 Repository hygiene
`SECURITY.md` (how to report a vulnerability), and a `CODEOWNERS` if the repo is to look maintained. Small, and reviewers notice their absence.

---

## 10. Milestone I — README, diagrams, and the cost model

**PR size: medium, docs only.** Land it **last** — it describes what the previous eight milestones built.

### 10.1 README correctness pass (the actual work here)
The current README overstates and understates in several places. Fix each:

| Claim | Reality |
|---|---|
| "8 hosts" | **Nine**: Gateway, Identity, Users, Orders, Restaurants, Notifications, Delivery, RealTime, Support. |
| Support service & ticketing — `📋 Pending` | Shipped: tickets, assignment under a distributed lock, audit log, message thread, refund requests, analytics summary (`docs/support-ticketing.md`). |
| Order placement idempotency — `📋 Pending` | Shipped: `PlaceOrder` takes an `Idempotency-Key` header and the Orders repository enforces it. |
| Production hardening — `📋 Pending` | This feature. |
| CI — `🚧 In Progress` | Still accurate. Say precisely what runs (build+test, actionlint, kubeconform, manifest policy, KinD smoke) and what does not (container publish, cloud deploy). |

Then add the sections this feature earns: a **Security** section pointing at `docs/security.md`, and an **API Documentation** section pointing at the Scalar URLs.

### 10.2 C3 message-flow diagram
The C1/C2 diagrams are current and stay. Add one C3-level Mermaid diagram of the **event topology** — which module publishes which integration event and which modules consume it, including the outbox → RabbitMQ → inbox hop. It is the single most distinctive thing about this codebase and there is no picture of it anywhere. Generate it from the `IntegrationEvents` projects and the `ConfigureConsumers` registrations, and consider a test asserting the diagram lists every `IntegrationEvent` type (the `ObservabilityAssetTests` pattern again) so it cannot silently go stale.

### 10.3 `docs/security.md`
The consolidated write-up: the authentication and authorization model (including §6.5's decision), the OWASP Top 10 pass with **what the platform does about each item and where the guardrail lives** — a table with a file path per row, not prose — secrets handling, the database privilege model, the TLS boundary, and an honest "known limitations" section (no TLS in-repo, no WAF, no penetration test, permissions cached for 5 minutes so a revoked permission has a lag).

### 10.4 `docs/cost-model.md`
The project plan's "final cost review" without an Azure subscription: take the measured capacity from `docs/load-testing.md` (the knee at host CPU 795–942% of 800%, 17,060 requests served at p95 554 ms with the rate limiter on) and derive a sizing table — node count/SKU for AKS, Azure Cache for Redis tier, PostgreSQL Flexible Server tier, and the two managed services that would replace in-repo components (Azure SignalR, Azure Monitor). Label every number as **derived from local measurements, not billed** — a fabricated Azure invoice is worth less than an honest extrapolation, and the extrapolation is the part that demonstrates the skill.

---

## 11. Out of scope

Named explicitly so a later reader does not go looking:

- **TLS certificates, an Ingress, a WAF.** The Kubernetes workstream was scoped down by the user; TLS terminates outside anything this repo deploys.
- **Penetration testing / DAST.** No deployed environment to point it at.
- **Azure provisioning of any kind.** §10.4 is a model, not a deployment.
- **The JWT role claim / gateway RBAC** — §6.5 documents the design instead; implementing it is a feature.
- **Reviews & ratings (2.6), Cosmos DB location history, Azure SignalR, Azure Monitor export, the AI features (3.1–3.3), FraudDetection (3.4).** All still pending, and this feature must document them as pending rather than quietly implying otherwise.

---

## 12. Milestone summary

| # | Milestone | Size | Depends on | Ships |
|---|---|---|---|---|
| A | Security audit as tests | M | — | Endpoint-auth coverage, gateway route/auth parity, IDOR sweep |
| B | Secret hygiene & scanning | S | — | gitleaks + allowlist, ConfigMap secret policy, duplicated-secret guard |
| C | Least-privilege database roles | M | — | Per-service owner/app roles, two connection strings, revoked cross-DB CONNECT |
| D | Edge hardening | M | — | `UseSecurityHeaders()`, forwarded headers, CORS for the SPA |
| E | Identity hardening | M | — | Signing-key management, fail-fast config, lockout, password policy |
| F | Input validation & error surface | S–M | — | Validator coverage test, bounded pagination, error-leak assertions |
| G | API documentation | M | — | Shared `AddApiDocumentation`, Scalar, gateway-proxied docs, completeness test |
| H | Supply chain | S | — | Dependabot, dependency-review, CodeQL, Trivy, SBOM, `SECURITY.md` |
| I | README, C3 diagram, security & cost docs | M | A, C | Correctness pass + `docs/security.md` + `docs/cost-model.md` |

Nine PRs. A–H are independent of each other; I closes the feature.
