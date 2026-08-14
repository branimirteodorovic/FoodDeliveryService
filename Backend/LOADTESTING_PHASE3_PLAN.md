# Feature 3.5 — Load Testing & Scalability Demonstration — Implementation Plan

> Ninth implementation plan, after `RESTAURANTS_PHASE1_PLAN.md`, `ORDERS_PHASE1_PLAN.md`,
> `NOTIFICATIONS_PHASE1_PLAN.md`, `DELIVERY_PHASE2_PLAN.md`, `REALTIME_PHASE2_PLAN.md`,
> `CACHING_PHASE2_PLAN.md`, `TELEMETRY_PHASE2_PLAN.md` and `KUBERNETES_PHASE2_PLAN.md`. This one
> covers **Feature 3.5 — Load Testing & Scalability Demonstration**, the fifth feature of **Phase 3**
> in `FoodDelivery_ProjectPlan.md`.

> **Status (verified against the code on 2026-08-08): not started.** There is no `k6`, no load
> script, no performance harness and no perf job in `.github/workflows/ci.yml` anywhere in the
> repository. Everything below is open work.

> **Scope for this iteration:** build a *repeatable, evidence-producing* load-testing capability
> against the running platform, and use it. Concretely: (1) a k6 harness that authenticates like a
> real client and measures what a real client experiences; (2) a deterministic seeded dataset,
> created through the **public API**, that makes order placement possible at all; (3) the three
> journeys the project plan names — browse → place order → track delivery — plus the driver side
> that closes the loop; (4) the four standard profiles (baseline, ramp, spike, soak) with pass/fail
> thresholds; (5) the results wired into the **existing** Prometheus + Grafana stack Feature 2.4
> already ships, so a run is read next to the system's own RED, business and cache panels; (6) a
> measured bottleneck hunt with the fixes it justifies; and (7) the write-up — `docs/load-testing.md`
> plus the README evidence the project plan asks for. Every milestone is one reviewable PR.

Decisions locked in for this plan:

- **Measure through the Gateway, like a client.** Hard Rule 10 says all external traffic goes
  through the Gateway; a load test that skips it measures a system nobody uses. The harness targets
  `:3000` by default. It *also* supports pointing a scenario straight at a service port (`:5200`,
  `:5300`) — not as the normal mode, but because "YARP hop cost" is a number worth having once, and
  the only way to get it is to run the same script both ways.
- **Never write to the database to set up a test.** The seeder uses `users/register`,
  `POST restaurants`, `POST delivery/drivers` and the real token endpoint. Inserting rows directly
  would skip the outbox, which means Orders would never receive the restaurant/menu replica and
  every seeded order would fail with `RestaurantNotFound` — the setup shortcut breaks the thing being
  measured. **One exception, and it is deliberate:** reading the one-time activation token out of the
  Users outbox, which is exactly what `Delivery.IntegrationTests.BaseIntegrationTest` already does
  (`GetActivationTokenAsync`) because the token otherwise only exists inside an invitation email.
- **Local-first, same as every plan before this one.** The runnable target is `docker-compose` (and,
  where it matters, the KinD cluster from 2.5). **Azure Load Testing — the service the project plan
  names — is an optional final milestone**, mirroring how 2.3/2.4 treated Azure Cache and Azure
  Monitor. It changes where the load is generated from, not what is measured.
- **Results land in the observability stack that already exists.** Feature 2.4 Milestone E shipped an
  OTel Collector, Prometheus (7-day retention), provisioned Grafana dashboards-as-code, six alert
  rules and a unit-test gate over all of it. The k6 run writes into that Prometheus via remote write
  and gets one more provisioned dashboard. No second metrics stack, no Grafana Cloud, nothing clicked
  together by hand.
- **The 100,000-user number is handled honestly.** See §11. Short version: a single-replica compose
  stack and a single load generator will not serve 100,000 concurrent users, and claiming otherwise
  in a README is the kind of thing an interviewer takes apart in ninety seconds. What this feature
  produces instead is *measured per-replica capacity, the saturation point, the specific component
  that saturates first, and a stated extrapolation* — which is a stronger answer, and it is the
  answer the milestones are built to support.
- Reference implementations to mirror: **`Delivery.IntegrationTests/Abstractions/BaseIntegrationTest.cs`**
  for the seeding sequence (onboard → activate → token) and every auth detail; **`docker/`** for how
  a new observability asset is added and gated; **`docs/observability-backend.md`** and
  **`docs/caching.md`** for the shape of the write-up this feature owes.

---

## 0. What already exists today (no work required)

**Eight hosts under `docker-compose`:** Gateway (`:3000`), Identity (`:18080`), Notifications
(`:5100`), Orders (`:5200`), Restaurants (`:5300`), Users (`:5400`), Delivery (`:5500`), RealTime
(`:5600`). Target framework `net10.0`.

| Capability this feature needs | Where | Status |
|---|---|---|
| A token endpoint a script can drive | Duende ROPC public client `fooddeliveryservice-public-client`, `RequireClientSecret = false`, scopes `openid profile email fooddeliveryservice.api` (`Identity/Config.cs`) | **shipped** — no Identity change needed |
| Anonymous customer registration | `POST users/register` (forced role `Customer`) | **shipped** |
| Read journey | `GET restaurants?page&pageSize`, `GET restaurants/{id}`, `GET restaurants/{id}/menu` | **shipped** |
| Write journey | `POST orders` with an `Idempotency-Key` header; server prices lines from the Orders-side menu replica | **shipped** |
| Track journey | `GET orders/{id}`, `GET delivery/orders/{orderId}/delivery` (live position from Redis GEO) | **shipped** |
| Driver journey | `POST delivery/drivers/me/location`, `PATCH delivery/drivers/me/availability`, `POST delivery/deliveries/{id}/{accept,picked-up,delivered}` | **shipped** |
| Metrics to compare a run against | `app_requests_total`, `app_request_duration_seconds_*`, `http_server_request_duration_seconds_*`, `orders_placed_total`, `orders_state_transition_total`, `delivery_assignment_outcome_total`, `delivery_assignment_duration_seconds_*`, `cache_hits_total`/`cache_misses_total`, `probe_success` | **shipped** (Telemetry A/B/E) |
| Prometheus + Grafana + provisioning + alerts | `docker/{prometheus,grafana,blackbox,otel-collector}`, `docker-compose.yml` | **shipped** (Telemetry E) |
| A gate that keeps dashboards honest | `Common.UnitTests/Observability/ObservabilityAssetTests` | **shipped** — and it will fail on a new dashboard until updated; see §7 |
| Correlation id honoured end to end, including across the outbox | `app.UseRequestCorrelation()` + `correlation_id`/`trace_parent` columns (Telemetry D + G) | **shipped** — a load run can stamp its own id and find itself in Seq and Jaeger |
| CI | `.github/workflows/ci.yml`: `build-and-test`, `tools` (actionlint, kubeconform, policy-check), `cluster` (KinD smoke, non-PR only) | **shipped** — no perf job |
| A cluster to measure scale-out on | `deploy/k8s` + `deploy/kind` — **one replica per service**, no HPA, no ingress | **shipped, deliberately not scaled** (2.5 §5.1) |

**Net:** the system is fully instrumented and observable, and *nothing generates load against it*.
Every number in this feature comes from the harness the milestones below build. No production code
change is required to *run* a load test; the changes in Milestones F and G exist because of what the
test finds.

---

## 1. Milestone overview

| # | Milestone | Layer touched | New surface | PR size |
|---|---|---|---|---|
| **A** | k6 harness foundation + smoke test | `Backend/loadtest/` (new) + `docker-compose` | k6 project skeleton, env config, ROPC auth helper, tagged HTTP wrapper, thresholds, `smoke.js`, compose `loadtest` profile, runner scripts | S–M |
| **B** | Deterministic seed fixture | `Backend/tools/…LoadTest.Seeder/` (new) | .NET console seeder driving the public API (customers, restaurants + menus, drivers), replica-arrival wait, `fixtures/seed.json` | M |
| **C** | The three journey scripts | `Backend/loadtest/scenarios/` | `browse.js`, `order.js`, `track.js` + `driver.js`, per-journey custom metrics and checks, `mixed.js` composition | M |
| **D** | Load profiles + runbook | `Backend/loadtest/config/` | baseline, ramp-to-saturation, spike, soak as data; per-profile thresholds; breaking-point method; `loadtest/README.md` | S–M |
| **E** | Results into Prometheus + Grafana | `docker/` + `Common.UnitTests` | k6 Prometheus remote write, `--web.enable-remote-write-receiver`, `fds-load` dashboard, `ObservabilityAssetTests` extended, `handleSummary` artifacts | M |
| **F** | Bottleneck round 1 — measured fixes | `Common.Infrastructure` + module config + `docker-compose` | connection-pool sizing, event-pipeline throughput (outbox interval/batch + `SKIP LOCKED`), the uncached browse list, permission-resolution storm — **whichever the data actually indicts** | M–L (split if needed) |
| **G** | Capacity guardrails at the edge | `Gateway` (+ `Common.Presentation`) | Redis-backed rate limiting + concurrency limits, `429` shaping, the Feature 1.3 gap the load test makes undeniable | M |
| **H** | The evidence: docs + README | `docs/load-testing.md`, `README.md`, `loadtest/results/` | methodology, numbers, graphs, bottleneck log, the honest extrapolation | S–M |
| **I** | *(optional)* CI performance smoke | `.github/workflows/ci.yml` | scheduled/dispatch job running the baseline profile with thresholds as the gate | S–M |
| **J** | *(optional)* Azure Load Testing / distributed k6 | `deploy/`/config | the same scripts executed from Azure at multi-region scale | M |

**Dependency order:** **A → B → C → D**, with **E** landing any time after A (it only needs *a* run to
have happened). **F and G must come after D + E** — they are responses to measurements, and a fix
merged before the baseline exists cannot be shown to have fixed anything. **H is last** by
definition. I and J are independent add-ons.

**The feature is complete after A–H.** A–E build the capability, F–G are what the capability is
*for*, H is the deliverable the project plan actually asks for ("include results in the GitHub README
as proof").

---

## 2. Milestone A — k6 harness foundation + smoke test

**Goal:** `k6 run smoke.js` authenticates as a real user, hits the read path through the Gateway,
and fails the process on a threshold breach. Everything after this milestone is scripts on top of it.

**Layout** (new tree, no existing file moves):

```
Backend/loadtest/
├── README.md                 # how to run; expanded in D
├── config/
│   ├── environments.js       # compose | compose-host | kind → gateway + identity base URLs
│   └── thresholds.js         # the shared SLO block; per-profile overrides land in D
├── lib/
│   ├── auth.js               # ROPC token acquisition + per-VU token reuse
│   ├── http.js               # tagged get/post wrappers, correlation id, standard checks
│   └── fixtures.js           # loads fixtures/seed.json in init context (B fills it)
├── scenarios/                # C
├── smoke.js
└── results/                  # gitignored except committed evidence (H)
```

**Tasks:**
- **Environment config, not hardcoded URLs.** Two modes that differ in a way that matters:
  `compose` (k6 runs *inside* the compose network → `http://fooddeliveryservice.gateway:8080`,
  `http://fooddeliveryservice.identity:8080`) and `compose-host` (k6 on the host → `:3000`/`:18080`).
  Select with `-e ENV=…`. The in-network mode is the default for real runs: it removes Docker's
  host port-forwarding from the measurement, and it is the only mode in which the service DNS names
  the rest of the stack uses actually resolve.
- **`lib/auth.js` — the single most important file in this milestone.** Duende ROPC:
  `POST {identity}/connect/token`, form-encoded, `grant_type=password`,
  `client_id=fooddeliveryservice-public-client`, `scope=openid profile email fooddeliveryservice.api`.
  Rules baked in:
  - **One login per VU, not one per iteration.** ASP.NET Identity hashes passwords with PBKDF2 and
    deliberately burns CPU doing it. A script that logs in every iteration turns the whole exercise
    into a password-hashing benchmark of one service, and Identity will be the "bottleneck" in every
    run for a reason that has nothing to do with the platform.
  - Cache the token in VU-local state with its `expires_in`; refresh only on expiry.
  - Tag token requests `{name: 'POST /connect/token'}` and give them their **own threshold**, so
    login cost is visible as a separate line rather than smeared into the journey.
- **`lib/http.js`** — thin wrappers that enforce three things every script gets wrong once:
  - **Bounded tag cardinality.** `http.get(url, {tags: {name: 'GET /restaurants/:id'}})`. Without the
    explicit name, k6 tags by full URL and every restaurant id becomes its own time series — the same
    rule §Observability in `CLAUDE.md` states for the server side, and it bites harder here because
    Milestone E ships these series to Prometheus.
  - **A correlation id per iteration.** Send `X-Correlation-Id: loadtest-{runId}-{vu}-{iter}`. The
    Gateway preserves an inbound value (Telemetry D), and Telemetry G carries it across the
    outbox/inbox boundary — so one Seq query pulls the full asynchronous fan-out of a specific
    synthetic order. This is worth more during the bottleneck hunt than any dashboard.
  - **Checks that fail loudly.** Status check *and* a body-shape check on at least one field. A
    `200 OK` carrying a `ProblemDetails` body is still an application failure, and a load test that
    counts it as success reports beautiful numbers for a broken system.
- **Thresholds as the pass/fail gate**, not decoration: `http_req_failed: ['rate<0.01']`,
  `http_req_duration: ['p(95)<500', 'p(99)<1500']` as the starting SLO, `checks: ['rate>0.99']`.
  Use `abortOnFail` on the error-rate threshold for the ramp profile in D so a run that has clearly
  fallen over stops instead of spending ten minutes recording zeros.
- **A compose `loadtest` profile.** Add a `fooddeliveryservice.k6` service (`grafana/k6:latest`)
  under `profiles: [loadtest]`, mounting `./loadtest` read-only, joined to the default network, with
  no port published. `docker-compose up -d` stays byte-for-byte unchanged for everyone;
  `docker compose --profile loadtest run --rm k6 run /loadtest/smoke.js` is the entry point.
  **Document the co-location caveat here, not later:** the generator and the system share one host's
  CPU, so above roughly half the host's cores the numbers describe the contest, not the platform.
- **Runner scripts** `loadtest/scripts/run.{sh,ps1}` — profile name in, environment variables and
  output flags out. Same dual-script convention as `deploy/kind/scripts`.

**Tests:** k6 scripts are not unit-testable in this repo's sense, and inventing a JS test project for
them is not worth it. The gate is `smoke.js` itself: ~5 VUs for 30 s over `GET /restaurants` +
`GET /restaurants/{id}` + `GET /restaurants/{id}/menu`, thresholds on. It is the thing every later
milestone runs first to prove the harness still works. `smoke.js` must also run green against an
**empty** database (the list returns `[]`), so it works before Milestone B exists.

**Done when:** `docker compose --profile loadtest run --rm k6 run /loadtest/smoke.js` passes against
a fresh `docker-compose up`, exits non-zero when a threshold is breached, and the run is findable in
Seq by its correlation-id prefix.

---

## 3. Milestone B — Deterministic seed fixture

**Goal:** one command produces a known dataset and a `fixtures/seed.json` the scripts read, so a
load run is reproducible and its failures mean something.

**Why this is a .NET console tool and not a k6 `setup()`:** two of the three actor types cannot be
created from k6 alone. Restaurant managers and drivers are **admin-provisioned by invitation** — the
one-time activation token is generated by Identity and delivered *by email*; the only programmatic
place it exists is the `UserInvitedDomainEvent` payload sitting in the Users **outbox table**. The
integration suites already solve this exact problem with a direct Npgsql read
(`BaseIntegrationTest.GetActivationTokenAsync`). Reusing that from C# is a short, honest tool;
reimplementing Postgres access inside k6 is neither.

**Tasks:**
- New project `Backend/tools/FoodDeliveryService.LoadTest.Seeder` (console, `net10.0`, added to
  `FoodDeliveryService.Api.slnx` under a new `/tools/` folder). It references **nothing** in `src/` —
  it is an API client, and a project reference would let it drift into calling domain code.
- Seed, in dependency order, all through the public API:
  1. **Admin token** — ROPC as `admin@fooddeliveryservice.com` (compose password `admin`, the KinD
     one is `Admin!23456`; take it from config, do not hardcode).
  2. **Restaurants** — `POST restaurants` (Administrator only), each with menu categories and items
     via `POST restaurants/{id}/menu-categories` + `/menu-items`. Default: 20 restaurants × 3
     categories × 8 items. Deterministic seed for Bogus so two runs produce the same catalogue.
  3. **Drivers** — `POST delivery/drivers`, then read the activation token from the Users outbox,
     then `POST users/accept-invitation` with a known password, then a ROPC login, then
     `PATCH delivery/drivers/me/availability` + an initial `POST delivery/drivers/me/location` so the
     Redis GEO set actually has candidates. Default: 50 drivers, positioned around the seeded
     restaurants (assignment is a radius search — drivers seeded in the wrong city produce a run
     where every order records `delivery_assignment_outcome{outcome="no_driver"}` and nothing else).
  4. **Customers** — `POST users/register` (anonymous, forced `Customer`). Default: 500. This is the
     slowest step by far because each one is a PBKDF2 hash; run it with bounded parallelism and say
     so in the log.
- **Wait for the replicas before declaring success.** This is the step that makes the difference
  between a working fixture and a load test that reports a 100% error rate for reasons nobody can
  find. `POST orders` fails unless *both* have arrived in the Orders database: the **customer**
  (`UserRegistered` → Orders inbox) and the **restaurant + menu items** (Restaurants → Orders
  replica). The outbox job ticks every **5 s** with a **batch of 20** (`MessageProcessor` in each
  host's `appsettings.Development.json`), so seeding 500 customers takes minutes to propagate, not
  seconds. The seeder polls a real endpoint — place one probe order per restaurant with a throwaway
  customer — and only writes the fixture once a probe succeeds, with a hard timeout and a clear
  error naming the outbox lag as the cause.
- **Emit `fixtures/seed.json`**: run id, timestamp, environment, and arrays of
  `{restaurantId, menuItemIds[]}`, `{email, password}` customers, `{email, password, driverId}`
  drivers. Committed? **No** — gitignored, with a tiny `seed.sample.json` checked in so the shape is
  reviewable and `lib/fixtures.js` has something to open in CI.
- Idempotency: re-running the seeder against an already-seeded database must not double the
  catalogue. Key on a deterministic email/tax-id prefix (`loadtest-…`) and skip what exists.

**Tests:** the seeder is verified by use — `smoke.js` extended to read the fixture and hit a seeded
restaurant's menu, plus one `--verify` mode on the tool itself that re-reads the fixture and asserts
every id still resolves through the API. No new xUnit project; the integration suites already cover
the endpoints it calls.

**Done when:** `dotnet run --project Backend/tools/FoodDeliveryService.LoadTest.Seeder` against a
fresh compose stack produces a fixture, and a single hand-run `POST orders` using it returns `200`
with an order id.

---

## 4. Milestone C — The three journey scripts

**Goal:** the user behaviour the project plan names, expressed as scripts that can be composed and
weighted.

**Tasks:**
- **`browse.js` (read-heavy, the volume journey).** `GET /restaurants?page=1&pageSize=20` → pick one
  → `GET /restaurants/{id}` → `GET /restaurants/{id}/menu`, with think time (`sleep(1–3 s)`,
  randomised) between steps. Note deliberately: **the list query is the only one of the three with no
  `ICachedQuery`** (`GetRestaurant` and `GetMenu` are cached, `GetRestaurants` is not), so this
  journey is also the experiment that decides Milestone F's caching question.
- **`order.js` (the write path).** Fixture customer → browse → `POST /orders` with 1–4 line items
  from that restaurant's menu.
  - **A fresh `Idempotency-Key` per iteration — `uuidv4()`, never a constant.** `PlaceOrderCommandHandler`
    looks the key up first and returns the existing order id if it hits. A script that reuses one key
    stops inserting rows after the first iteration and starts measuring a single indexed `SELECT`.
    That is a very fast, very impressive, completely fictional throughput number, and it is the single
    easiest way to accidentally lie in this feature.
  - Add a **small, tagged, deliberate** duplicate-key sub-case (~1% of iterations replay the previous
    key) so the dedupe path is exercised and measured *on purpose*.
- **`track.js` (the post-purchase journey).** Poll `GET /orders/{id}` and
  `GET /delivery/orders/{orderId}/delivery` every 3–5 s for a bounded number of polls. This is the
  journey with the worst read amplification per order and the one that keeps Redis busy (live driver
  position comes from the GEO store).
- **`driver.js` (the supply side — needed for anything downstream of `ready`).** Fixture driver →
  `POST /delivery/drivers/me/location` every 5 s → accept an offered delivery →
  `picked-up` → `delivered`. Without this scenario running, deliveries pile up in `Offered` and
  expire, and the order journey never reaches `Delivered` — the back half of
  `orders_state_transition_total` stays empty and the run tests a third of the platform.
- **Restaurant-side progression.** Orders sit in `Pending` unless someone accepts them. Either add a
  small `restaurant.js` acting as the manager (`accept` → `preparing` → `ready`) or drive it from the
  seeder as a background loop — **pick one and say which in the PR**; do not leave the lifecycle
  half-driven and then read the state-transition panel as if it were complete.
- **`mixed.js`** composes them with k6 `scenarios` and **`constant-arrival-rate`** executors, not
  `constant-vus`. Arrival rate is the right model for "N customers per second arrive"; VU-based
  closed loops silently throttle themselves as the system slows down, which hides exactly the
  degradation this feature exists to show. Weighting to start from: browse 70%, order 20%, track 8%,
  driver/restaurant as a fixed small pool sized to the order rate.
- Per-journey custom metrics (`Trend`/`Rate`/`Counter`) — `order_placement_duration`,
  `order_placement_failures`, `browse_to_order_conversion` — so a threshold can be stated per journey
  rather than only on the global `http_req_duration`.

**Tests:** each scenario runs standalone at low volume as its own smoke check; `mixed.js` at low
volume is the gate. Cross-check against the platform's own counters: after a run of N orders,
`orders_placed_total` must have advanced by N. If it hasn't, the script is measuring HTTP responses
that never became orders.

**Done when:** a 5-minute `mixed.js` run at a low arrival rate completes with all thresholds green,
and `orders_placed_total`, `orders_state_transition_total{to="Delivered"}` and
`delivery_assignment_outcome{outcome="offered"}` all move.

---

## 5. Milestone D — Load profiles + runbook

**Goal:** the four test types the project plan lists, as **data**, so adding a profile is a config
entry and not a new script.

**Tasks:**
- `config/profiles.js` exporting stage definitions selected by `-e PROFILE=…`:
  | Profile | Shape | Question it answers |
  |---|---|---|
  | `baseline` | low, constant, 5 min | What does an unloaded system cost per request? Everything else is read relative to this. |
  | `ramp` | step up until thresholds break, 10–20 min | **Where is the knee?** The number this whole feature is built to produce. |
  | `spike` | baseline → 10× for 60 s → baseline | Does it recover, and how long does the queue take to drain? |
  | `soak` | moderate, 1–2 h | Do memory, connections, or the outbox backlog grow without bound? |
- **Per-profile thresholds.** `baseline` and `soak` are strict (they must pass). `ramp` is
  deliberately expected to fail at the top — its threshold uses `abortOnFail` so the run stops at the
  knee and the last passing step *is* the answer.
- **The breaking-point method, written down** (this is the part that makes the results defensible):
  1. Fix the environment (compose vs KinD, replica count, host CPU/RAM) and record it with the run.
  2. Ramp arrival rate in steps, each held long enough for the caches and the outbox to reach steady
     state — **at least 60 s**, given a 5 s outbox tick.
  3. Declare saturation at the first step where p95 exceeds the SLO **or** `http_req_failed` exceeds
     1% **or** a queue/backlog metric grows monotonically across the whole step.
  4. Identify the saturated component from the platform's own telemetry — not from k6. RED per
     service, `cache_hits_total` vs misses, `delivery_assignment_outcome{outcome="lock_contended"}`,
     RabbitMQ queue depth, Postgres connection count.
  5. Record the number, the component, and one Jaeger trace of a slow request at that step.
- `loadtest/README.md`: how to run each profile, what to look at while it runs, what "good" is, and
  the co-location warning from A repeated where someone will actually read it.
- **Soak-run hygiene:** a 2-hour soak against compose fills Postgres with hundreds of thousands of
  orders and outbox rows. State the reset procedure (`docker compose down -v` + re-seed) and make the
  seeder's `loadtest-` prefix the thing that makes cleanup possible.

**Tests:** run `baseline` and `spike` end to end; keep their summary JSON as the first entries in
`results/`. `soak` is run once by hand — it is not something CI should ever start.

**Done when:** all four profiles run from one command each, `ramp` reliably identifies a knee, and
two consecutive `baseline` runs agree within a documented tolerance (if they don't, the harness is
measuring noise and nothing after this milestone is trustworthy).

---

## 6. Milestone E — Results into Prometheus + Grafana

**Goal:** a run is watched live and compared afterwards, next to the platform's own metrics, in the
Grafana that already exists.

**Tasks:**
- **k6 → Prometheus remote write.** Run k6 with `-o experimental-prometheus-rw` and
  `K6_PROMETHEUS_RW_SERVER_URL=http://fooddeliveryservice.prometheus:9090/api/v1/write`,
  `K6_PROMETHEUS_RW_TREND_STATS=p(95),p(99),avg,max`. Prometheus **rejects remote writes unless the
  receiver is enabled** — add `--web.enable-remote-write-receiver` to its `command:` in
  `docker-compose.yml`. Without that flag k6 reports write errors and the dashboard stays empty; it
  is a one-line change and a half-hour of confusion if missed.
- **A `fds-load` dashboard** in `docker/grafana/dashboards/load.json`, provisioned by the existing
  provider (no new mount). Panels: k6 arrival rate and VUs; `k6_http_req_duration` p95/p99 by
  `name` tag; `k6_http_req_failed` rate; **and, on the same rows, the server-side view** —
  `app_request_duration_seconds` p95 by service, `orders_placed_total` rate,
  `cache_hits_total`/`(hits+misses)`, `delivery_assignment_outcome_total` by outcome. Client-side and
  server-side latency on one axis is what makes the queueing visible: when the gap between them
  opens, the wait is in front of the application, not inside it.
- **`ObservabilityAssetTests` must be extended in the same PR — it will otherwise fail the build,
  by design:**
  1. `Dashboards_Should_BeProvisioned_ForEveryStoryTheMilestonePromises` asserts the dashboard uid set
     is **exactly** `{fds-red, fds-business, fds-cache}` (`BeEquivalentTo`). Add `fds-load`.
  2. `DashboardExpressions_Should_OnlyReferenceMetricsTheServicesEmit` rejects any metric name not in
     `KnownMetrics`. Add the `k6_*` series the dashboard queries, in a clearly commented block —
     these are *pushed by the load generator*, not emitted by a service, the same exception
     `probe_success` already occupies. Say that in the comment; the next person to read that list
     will otherwise assume something is missing.
  3. Every panel must name datasource uid `fooddelivery-prometheus`.
- **`handleSummary()` writes durable artifacts** — `results/{profile}-{runId}.json` plus a small
  markdown table. This is not optional polish: Prometheus keeps **7 days** and its volume is
  explicitly disposable, so the graphs behind the README (Milestone H) must be exported at run time
  or they are gone. Export the Grafana panels as PNG/snapshot in the same step and store them under
  `docs/assets/loadtest/`.
- Optional but cheap: one Prometheus alert rule that fires when the platform's error rate exceeds a
  threshold *during a run* — reusing the existing `alerts.yml` mechanism to prove the alerts work
  under real conditions rather than only when a container is killed by hand.

**Tests:** `Common.UnitTests` stays green with the extended `KnownMetrics` and uid set (that is the
test for this milestone). Plus: run `baseline`, then confirm every `fds-load` panel has data.

**Done when:** a running load test is visible live in Grafana, the run's summary JSON and panel
exports land in the repo, and `dotnet test Common.UnitTests` is green.

---

## 7. Milestone F — Bottleneck round 1 (measured fixes)

**Goal:** find what saturates first, fix what is worth fixing, and prove the fix with a before/after
of the same profile.

**This milestone is defined by the data, not by this list.** What follows is the shortlist of
*predicted* bottlenecks, derived from reading the code — each with the evidence that would confirm it
and the fix it implies. Confirm before fixing; a plausible bottleneck that isn't the actual one is
just a change with no story attached. **Split into multiple PRs if more than two of these land** —
one PR per fix, each with its own before/after.

| # | Predicted bottleneck | Evidence that confirms it | Fix |
|---|---|---|---|
| 1 | **Postgres connection exhaustion.** `postgres:17` in compose runs the default `max_connections=100`; six module hosts, each with Npgsql's default `Max Pool Size=100`, plus Identity, plus Dapper reads and the Quartz outbox/inbox jobs, all share it. Nothing in any connection string sets a pool bound. | `FATAL: sorry, too many clients already` in Seq; `pg_stat_activity` count pinned near 100; latency cliff rather than a curve. | Explicit `Maximum Pool Size` per service in the connection string (sum well under the server limit), raise `max_connections` in compose, and state the arithmetic in `docs/load-testing.md`. |
| 2 | **The event pipeline, not the API, is the real ceiling.** `MessageProcessor` is `IntervalInSeconds: 5`, `BatchSize: 20` in every host → a hard **~4 integration events/second per module**. Above that, `outbox_messages` grows without bound and order confirmations, delivery creation and notifications fall minutes behind while the API still answers in 50 ms. | Unprocessed outbox row count climbing monotonically through a ramp step; `orders_placed_total` rising while `delivery_assignment_outcome_total` flatlines. | Raise batch size / lower the interval as config (cheap), then add **`SKIP LOCKED`** to the `FOR UPDATE` in `ProcessOutboxJob`/`ProcessInboxJob` — the exact fix `KUBERNETES_PHASE2_PLAN.md` §5.1 already identified as the prerequisite for replicas taking disjoint batches. This is the highest-value finding in the plan and the one most worth writing up. |
| 3 | **`GetRestaurantsQuery` is the one uncached browse query.** `GetRestaurant`, `GetMenu` and `GetMenuItem` implement `ICachedQuery`; the **list** — the entry point of every browse iteration and the highest-volume request in the mix — does not. | `cache_hits_total` flat against the browse rate; `app_request_duration_seconds{request="GetRestaurantsQuery"}` dominating the per-request panel. | Make it an `ICachedQuery` keyed by page+pageSize via `RestaurantCacheKeys`, with **inline eviction** in the onboarding/update handlers (the pattern `docs/caching.md` mandates — never a domain-event handler). Note the trade-off in the PR: a list key has more invalidation triggers than an entity key. |
| 4 | **Permission-resolution storm.** Every authenticated request runs `CustomClaimsTransformation` → `IPermissionService`; in the five non-Users services that is a **MassTransit RPC to Users**, cached in Redis for 5 minutes. A ramp introducing thousands of distinct users produces thousands of cold-cache RPCs, and Users becomes the bottleneck for services that never touch it. | `app_request_duration_seconds{request="GetUserPermissionsRequest"}` and Users' RED rising with *other* services' traffic; RabbitMQ RPC queue depth. | Measure first. Options in ascending cost: longer TTL, jittered expiry to avoid synchronised stampedes, or putting permissions in the token as a claim (a real design change — propose, don't smuggle it in). |
| 5 | **Redis is one instance carrying four workloads** — cache, `IDistributedLock`, Delivery's driver GEO set, SignalR backplane — and it is an availability dependency, not an optimisation. | Redis CPU at ceiling; `delivery_assignment_outcome{outcome="lock_contended"}` climbing; `GEO` command latency in the Delivery spans. | Probably no code fix — the honest outcome is a documented capacity limit and the note that it stays a **single logical instance** because clustering breaks the lock's guarantee (2.5 §2). |
| 6 | **Identity's token endpoint is CPU-bound by design.** PBKDF2 hashing. | Identity CPU pinned while its request rate looks trivial. | Harness-side already handled in A (one login per VU). Platform-side: record it as a real capacity fact — token issuance is the most expensive endpoint per request in the system. |
| 7 | **Assignment lock contention.** The 5 s `IDistributedLock` TTL around offer/accept was sized for human-paced traffic. | `lock_contended` rate rising with order rate. | The existing counter is the early warning 2.4 Milestone B built for exactly this. Tune the TTL only with the data in hand. |

**Method (non-negotiable, or the milestone produces opinions instead of results):** for each fix —
run the profile, record, change **one** thing, re-run the *same* profile in the *same* environment,
record, and put both numbers in the PR description. Anything that doesn't improve the number gets
reverted, and gets a line in `docs/load-testing.md` saying it didn't help. Negative results are the
most credible content in a document like this.

**Done when:** the saturation point has moved measurably, each merged change carries a before/after,
and every existing suite is still green (Milestone F touches shared infrastructure — `Common.UnitTests`,
`Orders`/`Delivery`/`Restaurants` integration suites all re-run).

---

## 8. Milestone G — Capacity guardrails at the edge

**Goal:** the platform degrades instead of collapsing. A load test against a system with no admission
control mostly proves how quickly you can knock it over yourself.

**The gap is pre-existing and documented:** `KUBERNETES_PHASE2_PLAN.md` §7 records that **the Gateway
has no rate limiter — a Feature 1.3 task that was never built.** Milestone D's ramp will make that
concrete: past the knee, everything queues, every client times out, and no request is served rather
than most requests being served.

**Tasks:**
- **Rate limiting in the Gateway**, via ASP.NET Core's built-in rate limiter in front of YARP: a
  per-client partition (subject claim when authenticated, IP otherwise), a global concurrency limit,
  and `429` with `Retry-After`.
- **It must be Redis-backed, or it is a lie under replicas.** 2.5 §5.4 states this explicitly: the
  Gateway can scale freely today only because no limiter exists; per-pod in-memory buckets multiply
  the effective limit by the replica count. The Redis multiplexer from `AddInfrastructure` already
  exists — but note the Gateway does **not** take a `Common.Infrastructure` dependency (2.4 Milestone
  A), so the wiring is a deliberate, small, explicit addition, not a copy of the module-host setup.
- **Exempt what must not be limited:** `/health/live`, `/health/ready` (the blackbox exporter probes
  every host every 15 s and a throttled probe is a false outage alarm), and the SignalR `hubs/**`
  negotiate/connect path.
- **Shed load in a shaped way**, not uniformly: a `429` on `GET /restaurants` is a slightly worse
  browse; a `429` on `POST /delivery/deliveries/{id}/delivered` strands a real delivery. Rank the
  routes in the PR.
- **Re-run the ramp with the limiter on** and show the difference: throughput plateaus instead of
  collapsing, p95 for admitted requests stays inside the SLO, and the rejected fraction is explicit
  rather than expressed as timeouts. That graph is the single best artifact this whole feature
  produces.
- Update `smoke.js`/`mixed.js` to treat `429` as an expected-and-counted outcome above a given rate,
  not as a failed check — otherwise the guardrail fails the test that motivated it.

**Tests:** integration test in an existing suite driving the limiter (N requests → the N+1th is
`429` with `Retry-After`); a unit test for the partition key selection (authenticated → subject,
anonymous → IP). Both cheap, both catch the two ways this is usually wrong.

**Done when:** the limiter is on by default with config-tunable limits, health and hub paths are
exempt, and the ramp profile shows a plateau rather than a cliff.

---

## 9. Milestone H — The evidence

**Goal:** what the project plan actually asks for — *"include results (graphs, numbers) in the GitHub
README as proof."*

**Tasks:**
- **`Backend/docs/load-testing.md`** — the reference doc, same role `docs/caching.md` and
  `docs/observability-backend.md` play for their features: how to seed, how to run each profile, what
  each threshold means, the exact environment every published number came from (host CPU/RAM, replica
  count, compose vs KinD, k6 co-located or not), and the full bottleneck log **including the fixes
  that didn't work**.
- **README section** — a short table (RPS, p50/p95/p99, error rate, per journey), two or three
  graphs, and the saturation point with the component that saturated. Link to the doc for the method.
- **The extrapolation, stated as an extrapolation.** "X orders/second per replica, measured; the
  event pipeline saturates at Y; scale-out to Z replicas is blocked on the three hazards in
  `KUBERNETES_PHASE2_PLAN.md` §5.1, one of which this feature fixed." A recruiter reading that learns
  more about the engineer than any six-figure user count would.
- Cross-link from `KUBERNETES_PHASE2_PLAN.md` §5.1 (which predicted #2 in the shortlist above) and
  `docs/observability-backend.md` (which now has a fourth dashboard).

**Done when:** a reader who has never run the project can reproduce a published number from the
documentation alone.

---

## 10. Optional milestones

- **I — CI performance smoke.** A `perf` job on `workflow_dispatch` + a nightly schedule: bring up
  compose on the runner, seed a small fixture, run `baseline`, fail on thresholds. It must **not**
  run per-PR: a shared GitHub runner is noisy, and a flaky perf gate gets disabled within a week,
  taking the real signal with it. Same reasoning that keeps the KinD `cluster` job off pull requests.
  Value: catches gross regressions (an accidental N+1, a lost cache) rather than measuring capacity.
- **J — Azure Load Testing / distributed k6.** The project plan names Azure Load Testing; it runs
  these same scripts from managed multi-region infrastructure, which removes the single-generator
  ceiling *and* the co-location skew. It requires a publicly reachable deployment, so it depends on
  2.5 §5.7 (AKS), which does not exist. The local alternative if scale is the only goal is the k6
  Operator on the KinD cluster — several generator pods, one aggregated result. Neither changes a
  single line of the scripts, which is the point of building them the way A–D do.

---

## 11. What "100,000 users" means here, and what will actually be claimed

The project plan says *"gradually increase to 100,000 virtual users"*. Stated plainly, before anyone
writes it in a README:

- **A single-replica compose stack will not serve 100,000 concurrent users.** One Postgres, one
  Redis, one RabbitMQ, one instance of each service, all on one developer machine, with an event
  pipeline configured at ~4 events/second/module. The knee will be orders of magnitude below that
  number, and finding exactly where is the useful part.
- **One k6 process is also a limit.** Tens of thousands of VUs with think time is feasible for simple
  GETs on a well-resourced machine; it is not feasible while that same machine runs eight .NET
  services, Postgres, Redis, RabbitMQ, Prometheus, Grafana, Jaeger and Seq.
- **So the claim is a measured one, with its scope attached:** *"Measured at N requests/second and M
  orders/second per replica at p95 = X ms; the first component to saturate was C; the path to
  100,000 concurrent users is horizontal scaling, and here is precisely what blocks it in this
  codebase today."*
- If a big number is genuinely wanted for the README, the honest route is Milestone J against a real
  multi-replica deployment — earned, not asserted.

An interviewer who asks *"how do you know it handles real traffic?"* is satisfied by a number with a
method behind it and unsatisfied by a large number without one. Every milestone above is built for
the first answer.

---

## 12. Explicitly out of scope

- **SignalR / WebSocket load** (`hubs/**`). k6 can drive raw WebSockets, but SignalR's
  negotiate-then-connect handshake and its protocol framing make it a milestone of its own, and
  RealTime is pinned to **one replica** for the negotiate-affinity reason in 2.5 §5.4 — so the
  measurement's conclusion is already known and already documented. The `track.js` journey polls the
  REST tracking endpoint instead, which is a real client behaviour and the one that loads the
  database and Redis. Revisit if Azure SignalR Service is ever adopted.
- **Frontend / browser-level performance.** `Frontend/FRONTEND_PLAN.md` is a separate worktree and
  Lighthouse-style metrics answer a different question.
- **Chaos/fault injection** (killing a broker mid-run). Adjacent and valuable; not this feature. The
  KinD smoke test already covers the one case that matters most — probe behaviour under a real
  dependency outage.
- **Database benchmarking in isolation** (pgbench). The interesting question is the platform's
  behaviour, not Postgres'.
- **Multi-replica scale-out measurements.** They require 2.5 §5.1's three hazards to be fixed first
  (migration race, `FOR UPDATE` without `SKIP LOCKED`, the double-counting expired-offer job). This
  feature fixes at most the second, as Milestone F #2, because that one is also a single-replica
  throughput fix.

---

## 13. Repo facts worth keeping

- **ROPC is available and needs no secret**: `fooddeliveryservice-public-client`,
  `AllowedGrantTypes = ResourceOwnerPassword`, `RequireClientSecret = false`, scopes
  `openid profile email fooddeliveryservice.api`. The confidential client is client-credentials only,
  scoped to `users:register`, and is not what a load script wants.
- **Compose admin is `admin@fooddeliveryservice.com` / `admin`; the KinD cluster's is `Admin!23456`**
  (non-Development environments apply ASP.NET Identity's real password rules — 2.5 §2).
- **`POST orders` takes an optional `Idempotency-Key` header** and dedupes on it via a unique index.
- **Order placement needs two replicas to have arrived** in the Orders database — customer (from
  `UserRegistered`) and restaurant + menu items — both via the 5 s / 20-row outbox.
- **`GetRestaurants` (list) is not cached**; `GetRestaurant`, `GetMenu`, `GetMenuItem` are.
- **Managers and drivers are invitation-provisioned**; the activation token exists programmatically
  only in the Users outbox payload (`BaseIntegrationTest.GetActivationTokenAsync` is the working
  precedent).
- **Prometheus retains 7 days** on a disposable named volume, and **remote write is off by default** —
  it needs `--web.enable-remote-write-receiver`.
- **`ObservabilityAssetTests` pins the dashboard uid set exactly** and rejects unknown metric names in
  any dashboard or alert expression. A new dashboard is a two-file change, always.
- **The Gateway had no rate limiter and has no `Common.Infrastructure` dependency** — both shaped
  Milestone G, which built one: the shared code lives in `Common.Presentation/RateLimiting` (the same
  place the health probes and `AddHostTelemetry` live, for the same reason) and only the Redis store
  is in the Gateway. See [`docs/rate-limiting.md`](docs/rate-limiting.md).
- **Every service runs one replica**, applies migrations at startup, and exports OTLP to the
  collector at `http://fooddeliveryservice.otel-collector:4317`.
