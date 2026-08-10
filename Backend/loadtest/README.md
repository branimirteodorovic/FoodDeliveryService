# Load testing

The k6 harness for **Feature 3.5 — Load Testing & Scalability Demonstration**
(`../LOADTESTING_PHASE3_PLAN.md`). This document covers **Milestone A** (the foundation and the smoke
test), **Milestone B** (the deterministic seed fixture) and **Milestone C** (the journey scripts).
Milestone D expands it into the full runbook (profiles, the breaking-point method, what to watch
while a run is in flight).

## Run it

The stack must already be up (`docker-compose up -d` from `Backend/`). Then, from `Backend/`:

```bash
docker compose --profile loadtest run --rm fooddeliveryservice.k6 run /loadtest/smoke.js
```

Or, from `loadtest/scripts/`, which fills in the run id and the summary export:

```bash
./run.sh
```

```powershell
./run.ps1
```

`docker-compose up -d` is unchanged for everyone who is not load testing — the generator sits behind
the `loadtest` compose profile and only starts when asked for by name.

Useful variations:

| Command | What it does |
|---|---|
| `./run.sh --local` | Uses the `k6` binary on PATH against the published ports (`:3000`/`:18080`) |
| `./run.sh --env kind` | Targets the Feature 2.5 KinD cluster (Gateway `:8000`) |
| `./run.sh --run-id nightly-01` | Names the run — every correlation id carries it |
| `./run.sh -- --vus 20 --duration 2m` | Everything after `--` goes straight to k6 |
| `GATEWAY_URL=http://fooddeliveryservice.restaurants.api:8080 ./run.sh` | Bypasses the Gateway, to price the YARP hop |

## Seed the dataset first

`smoke.js` runs against an empty database, but nothing that places an order can. From `Backend/`:

```bash
dotnet run --project tools/FoodDeliveryService.LoadTest.Seeder
```

That produces 20 restaurants × 3 categories × 8 items, 50 drivers (clocked on, positioned, in
Redis' GEO set) and 500 customers — all through the public API — and writes `fixtures/seed.json`,
which `lib/fixtures.js` reads in init context. The defaults and every switch:

```bash
dotnet run --project tools/FoodDeliveryService.LoadTest.Seeder -- --help
```

The tool runs **on the host** and so defaults to `:3000` / `:18080` / `localhost:5432`; the fixture
it writes is still valid for a k6 run inside the compose network, because both address the same
database. Point it at the KinD cluster with `--gateway`, `--identity`, `--users-connection`,
`--admin-password` and `--environment kind`.

Things worth knowing before the first run:

- **It takes minutes, and most of that is two things.** 500 registrations are 500 PBKDF2 hashes on
  Identity's CPU, and after the last one the seeder *waits* — it places a probe order against every
  seeded restaurant and refuses to write the fixture until one succeeds. `POST orders` needs the
  customer and the restaurant + menu replicas to have reached the **Orders** database through the
  outbox, which ticks every 5 s in batches of 20 per module. A fixture written before that is a
  fixture of ids the order path cannot resolve, and the symptom is a load run reporting a 100% error
  rate nobody can explain.
- **Re-running is safe.** Everything is keyed on the `loadtest-` prefix (emails) and the
  `LOADTEST-nnnn` tax identification (restaurants), so a second run reuses what exists instead of
  onboarding a second catalogue. A run interrupted half way resumes: an account that was onboarded
  but never activated is picked up from its invitation token.
- **Same seed, same world.** `--random-seed` drives Bogus, and the whole dataset is generated before
  the first HTTP call, so parallel execution cannot make two runs differ.
- **It never writes to a database.** One exception, deliberate: reading an invited driver's one-time
  activation token out of the Users outbox, because outside an email that is the only place it
  exists. `Delivery.IntegrationTests.BaseIntegrationTest` does exactly the same thing. Inserting rows
  directly would skip the outbox, so Orders would never receive the restaurant replica and every
  seeded order would fail with `RestaurantNotFound` — the setup shortcut breaks the thing being
  measured.

To check a fixture that has been sitting around — the cheap answer to "did somebody run
`docker compose down -v` since this was written?":

```bash
dotnet run --project tools/FoodDeliveryService.LoadTest.Seeder -- --verify
```

`seed.json` is gitignored (it holds throwaway credentials and ids that mean nothing against another
database); `seed.sample.json` is committed next to it so the shape stays reviewable. After a soak
run, the reset is `docker compose down -v` followed by a re-seed.

## What the smoke test is for

Five VUs for thirty seconds over the read path — `GET /restaurants`, `GET /restaurants/{id}`,
`GET /restaurants/{id}/menu` — through the Gateway, with thresholds armed.

It is **not** a capacity measurement. It is the check every later milestone runs first, to prove that
auth, tagging, correlation and the pass/fail gate still work before spending twenty minutes on a
ramp. It exits non-zero when a threshold is breached, so it can be a gate rather than a report.

It also runs green against an **empty** database: the list returns `[]` and the iteration ends after
one request. That is deliberate — the harness has to be reviewable before Milestone B's seeder exists.

What it measures today, against a warm compose stack with the generator co-located (5 VUs, 30 s,
empty catalogue):

| | p95 | p99 | notes |
|---|---|---|---|
| journey (`{scope:journey}`) | 33 ms | 81 ms | list → detail → menu, through the Gateway |
| login (`{scope:auth}`) | 643 ms | — | 5 concurrent logins; PBKDF2, and it shows |
| errors | 0% | | 234/234 checks passed |

### Why there is a warm-up

`setup()` runs one full journey before any VU starts, tagged `scope: setup`.

Measured on this stack: the **first** authenticated request against a freshly started service takes
~2.5 s inside Restaurants (~3.0 s at the Gateway), against 15–40 ms in steady state. The cost is the
cold path behind `CustomClaimsTransformation` → `IPermissionService` — the MassTransit RPC to Users
and its RabbitMQ topology, plus an empty Redis permission cache. The first token request against a
cold Duende took 7.9 s. With five VUs all issuing their first request at once, five 3-second samples
out of ~70 put journey p95 at 3 s and failed a run in which every other request was fast.

Those numbers are real and worth knowing — the permission-resolution path is Milestone F #4 on the
bottleneck shortlist — so they are **measured, not hidden**: the warm-up appears in the summary under
`{scope:setup}`. It is simply not what the journey SLO is about. Process cold start is a deployment
property; the thresholds are about what a user experiences against a running system.

## The journeys

Five scripts under `scenarios/`. Each runs standalone at low volume as its own smoke check, and each
exports its journey so `mixed.js` can compose them — the journey logic lives in one place and only
the *amount* of it differs.

```bash
./run.sh scenarios/mixed.js            # everything at once — the Milestone C gate
./run.sh scenarios/browse.js           # or any one journey on its own
```

| Script | Who | What it does | Why it is in the mix |
|---|---|---|---|
| `browse.js` | customer | list → detail → menu, with think time | 70% of customer traffic, and the experiment that decides Milestone F's caching question — of its three requests only the **list** has no `ICachedQuery` |
| `order.js` | customer | browse, then `POST /orders` with 1–4 lines | the write path; ~1% of iterations replay the previous `Idempotency-Key` on purpose |
| `track.js` | customer | poll `GET /orders/{id}` + the delivery every 3–5 s | worst read amplification per order, and the journey that keeps Redis busiest |
| `restaurant.js` | manager | dashboard poll → one lifecycle step per order | **without it nothing leaves `Pending`** and two thirds of the platform is never exercised |
| `driver.js` | driver | position report → claim an offer → picked-up → delivered | **without it nothing reaches `Delivered`** |
| `mixed.js` | — | all five, weighted | the only shape that measures the platform rather than one endpoint of it |

### Arrival rate, not virtual users

The three customer journeys run under `constant-arrival-rate`. A `constant-vus` closed loop issues
its next request only after the previous one returns, so as the system slows the offered load falls
with it — throughput plateaus, latency looks merely elevated, and the run reports a system coping.
That is backwards: real customers do not slow down because the site is slow. Arrival rate is what
lets Milestone D's ramp find a knee at all.

The two operator scenarios are `constant-vus`, correctly: a kitchen has a fixed number of staff and a
city a fixed number of drivers. They are supply, not demand.

### Knobs

| | Default | |
|---|---|---|
| `RATE` | `2` | total customer arrivals per second, split browse 70% / order 20% / track 8% |
| `DURATION` | `5m` | |
| `KITCHEN_VUS` | one per seeded restaurant | fewer leaves the restaurants it does not cover stuck in `Pending` all run |
| `DRIVER_VUS` | `8` | see *the offer-board workaround* below before raising it |
| `ORDER_REPLAY_RATE` | `0.01` | share of orders that deliberately replay the previous idempotency key |

### The one way to accidentally lie in this feature

`PlaceOrderCommandHandler` looks up `Idempotency-Key` **first** and returns the existing order id if
it hits — before the customer, the restaurant replica, the pricing or the insert. A script that
reuses one key stops creating orders after its first iteration and starts measuring a single indexed
`SELECT`. Throughput rises, latency collapses, the summary looks spectacular, and none of it
happened.

So `order.js` mints a fresh key per placement, and its `orders_placed` counter exists to be compared
against the platform's own `orders_placed_total` after a run. If they disagree, the script is
measuring HTTP responses that never became orders.

The dedupe path is real behaviour worth measuring — a mobile client retrying over a flaky connection
— so ~1% of iterations replay the previous key deliberately, tagged `placement:replay`, and checked
for the property that matters: the replay must return the **same** order id.

### The offer-board workaround, and what it found

**A driver cannot discover over REST that they have been offered a delivery.** Verified against the
code: `GET /delivery/deliveries` filters on `driver_id`, which is `NULL` until a driver *accepts*;
`offered_driver_id` appears in no response DTO; `DeliveryAccess.EnsureCanView` admits the customer,
the *assigned* driver and administrators, not the offered one; and `DeliveryOfferedIntegrationEvent`
exists but nothing consumes it yet. The offer reaches a real driver app through SignalR, which this
feature puts out of scope (§12).

`driver.js` therefore polls the **administrator's** delivery board for `Offered` deliveries and
attempts one per tick, oldest first. The domain rejects every driver but the offered one, so exactly
one claim wins. The cost is roughly *P/2* wasted claims per delivery for a pool of *P* — bounded
(each VU tries a delivery once), counted (`driver_claims_missed`, `driver_claim_hit_rate` ≈ 1/*P*),
tagged `scope: dispatch` so it stays out of the journey SLO, and the reason the driver pool is
deliberately small. The honest fix is a "my offers" read model or the SignalR push, and it belongs in
the Delivery feature, not in a load test.

Building it surfaced two platform findings worth carrying into Milestone F:

1. **The available-driver pool is polluted by anyone who stops reporting.**
   `delivery:drivers:available` is a Redis GEO set with no per-member TTL, and freshness lives in a
   separate 60 s key — but `GEOSEARCH` applies `count: CandidateLimit` (10) **before** the freshness
   filter. With 50 seeded drivers and 8 driven, the ten nearest members are almost all stale, the
   filter discards them, and the offer routine sees *no candidates at all*. Measured: 48 deliveries
   `Unassigned` against 37 delivered, **34 of them never offered to anyone**. In production every
   driver who closes the app leaves a permanent member, and once those outnumber `CandidateLimit`
   near a restaurant, orders there stop being assignable. `driver.js` works around it by clocking
   off the seeded drivers the run is not driving (going offline `ZREM`s the member): the same run
   then produced **55 delivered against 7 unassigned**.
2. **`Unassigned` is terminal and fires on a momentary shortage.** When every currently-available
   candidate has been tried — including when the list is empty because they are all busy — the
   delivery is parked for a human immediately. There is no "wait and retry" state, so a transient
   dip in driver supply strands an order permanently. This is why `DRIVER_VUS` has to be sized
   against `RATE` and not chosen in the abstract.

### Reading a mixed run

Cross-check the client's view against the platform's own counters — that is the whole point of
running against an instrumented system:

| k6 says | The platform must agree |
|---|---|
| `orders_placed` | `orders_placed_total` advanced by the same amount |
| `deliveries_completed` | `orders_state_transition_total{to="Delivered"}` moved |
| `driver_claims_won` | `delivery_assignment_outcome_total{outcome="offered"}` moved |
| `track_delivery_visible` near zero | the kitchen side is not running — the lifecycle is half-driven |

## Layout

```
loadtest/
├── config/
│   ├── environments.js   compose | compose-host | kind → gateway + identity URLs, credentials, run id
│   └── thresholds.js     the shared SLO block and the scope tags
├── lib/
│   ├── auth.js           ROPC token acquisition, cached per VU
│   ├── http.js           tagged request wrappers, correlation id, checks, expected statuses
│   ├── fixtures.js       reads fixtures/seed.json; the VU → identity round-robin
│   ├── actors.js         who a VU is: its customer, driver or manager, and its token
│   ├── domain.js         the wire format — status enums, payload builders
│   └── metrics.js        the per-journey custom metrics
├── fixtures/
│   ├── seed.json         written by tools/FoodDeliveryService.LoadTest.Seeder — gitignored
│   └── seed.sample.json  the same shape, committed, so a diff can review the format
├── scenarios/
│   ├── browse.js  order.js  track.js     the three customer journeys
│   ├── restaurant.js  driver.js          the supply side that closes the lifecycle
│   └── mixed.js                          all five, weighted
├── scripts/run.{sh,ps1}  the runners
├── smoke.js
└── results/              run artifacts, gitignored except results/published/
```

## Environments

| `-e ENV=` | Gateway | Identity | When |
|---|---|---|---|
| `compose` *(default)* | `http://fooddeliveryservice.gateway:8080` | `http://fooddeliveryservice.identity:8080` | k6 **inside** the compose network — the mode every published number should come from |
| `compose-host` | `http://localhost:3000` | `http://localhost:18080` | k6 on the host, while writing a script |
| `kind` | `http://localhost:8000` | `http://localhost:18080` | the Feature 2.5 cluster |

`compose` is the default because it removes Docker's host port-forwarding from the measurement, and
because it is the only mode in which the service DNS names the rest of the stack uses resolve.

## Five things the harness enforces, and why

**One login per VU, not one per iteration.** ASP.NET Identity hashes passwords with PBKDF2 and
deliberately burns CPU doing it. A script that logs in every iteration turns the exercise into a
password-hashing benchmark of one service: Identity pins a core, everything queues behind it, and
every run "finds" the same bottleneck for a reason that has nothing to do with the platform.
`lib/auth.js` caches the token in VU-local state and refreshes only on expiry. Token requests carry
their own tag (`POST /connect/token`) and their own threshold, so login cost is a visible line rather
than something smeared into the journey percentiles.

**Bounded tag cardinality.** `lib/http.js` refuses a request without an explicit `name`. Without one,
k6 tags by full URL and every restaurant id becomes its own time series — the same rule `CLAUDE.md`
states for the server side, and it bites harder here because Milestone E ships these series into the
platform's own Prometheus. Use the route template: `GET /restaurants/:id`.

**A correlation id per iteration.** Every request carries
`X-Correlation-Id: loadtest-{runId}-{vu}-{iteration}`. The Gateway preserves an inbound value
(Telemetry D) and Telemetry G carries it across the outbox/inbox boundary onto the `correlation_id`
column, so this Seq query pulls the full asynchronous fan-out of one synthetic request — including
the legs that happen seconds later in another service:

```
CorrelationId like 'loadtest-<run-id>-%'
```

During the Milestone F bottleneck hunt that is worth more than any dashboard.

**Checks that fail loudly.** Status *and* a body-shape check on at least one field, plus a guard
against a `2xx` carrying a `ProblemDetails` body. A load test that counts an application failure as a
success reports beautiful numbers for a broken system.

**Expected non-2xx outcomes are declared, not tolerated.** Two journeys legitimately receive a 4xx:
`track.js` polls a delivery that does not exist until the restaurant marks the order ready (`404`),
and `driver.js` claims offers it may not have been given (`400`). Passing `status: [200, 404]` to the
wrapper both records the check correctly and hands k6 a `responseCallback`, so `http_req_failed` keeps
meaning *"the platform answered something nobody asked for"*. The alternative — loosening the error
threshold until the expected failures fit under it — would blind the run to the real ones.

**Pacing belongs inside an operator journey, not in its caller.** `restaurant.js` and `driver.js` run
under `constant-vus`, an open loop with nothing to pace them, so a journey that returns immediately
simply starts again. Measured, with the sleep left in the standalone `default` and omitted from the
composed path: 20 kitchen VUs and 8 driver VUs alone produced **221 requests per second**, forty
times the customer traffic they exist to support, and every percentile in that run described the
polling loop instead of the journeys.

## Thresholds

`config/thresholds.js`, applied by every script:

| Metric | Gate |
|---|---|
| `http_req_failed` | `rate<0.01` |
| `http_req_duration{scope:journey}` | `p(95)<500`, `p(99)<1500` |
| `http_req_duration{scope:auth}` | `p(95)<2000` |
| `checks` | `rate>0.99` |

Scenarios add their own on top, which is the point of the custom metrics: in a mixed run the global
`http_req_duration` is dominated by browse at 70% of the traffic, so it stays green while the write
path degrades.

| Metric | Gate | Where |
|---|---|---|
| `order_placement_duration` | `p(95)<1000` | `order.js`, `mixed.js` |
| `order_placement_failures` | `rate<0.01` | `order.js`, `mixed.js` |
| `order_idempotency_replay_correct` | `rate>0.99` | `order.js`, `mixed.js` — a replay that returns a *different* order id means the dedupe is broken and retries are creating duplicate orders |
| `kitchen_transition_success` | `rate>0.95` | `restaurant.js`, `mixed.js` |
| `driver_claim_hit_rate` | `rate>0` | `mixed.js` only — zero means nothing reached `Delivered` and the run measured browsing with extra steps |

`mixed.js` raises the login budget to `p(95)<4000` for itself, and the arithmetic is worth stating
rather than hiding: the run starts ~58 VUs and each one's first iteration acquires a token, so
Identity is handed roughly sixty PBKDF2 verifications inside the first second or two. Measured p95
2.87 s, median 1.09 s, against 643 ms for smoke.js's five concurrent logins and ~150 ms for one. It is
a **startup transient that scales with the VU count**, not the steady-state cost of signing in. It is
raised rather than excluded because token issuance being the most expensive endpoint in the system is
a real capacity fact (Milestone F #6); Milestone D replaces the guess with numbers measured from a
ramp whose VUs arrive gradually.

These are a starting SLO — chosen, not measured. Milestone D's baseline run is what turns them into
numbers with evidence behind them, and adds per-profile overrides (`ramp` uses `abortOnFail`, so a run
that has clearly fallen over stops at the knee instead of spending ten minutes recording zeros).

## Read this before quoting a number

**The generator shares the host with the system under test.** k6, eight .NET services, Postgres,
Redis, RabbitMQ, Prometheus, Grafana, Jaeger and Seq all run on one machine. Above roughly half the
host's cores, the results describe that contest and not the platform. Every published number has to
carry the environment it came from — host CPU/RAM, replica count, compose or KinD, generator
co-located or not. Milestone H's `docs/load-testing.md` is where that record lives.

## Credentials

Scripts default to the compose admin seed (`admin@fooddeliveryservice.com` / `admin`), because it is
the only account guaranteed to exist against a database nobody has seeded. Override with
`LOADTEST_USERNAME` / `LOADTEST_PASSWORD` — the KinD cluster applies ASP.NET Identity's real password
rules and its admin password differs. From Milestone B onwards, scenarios take their users from
`fixtures/seed.json` instead.

The prefix matters: k6 folds the whole system environment into `__ENV`, and `USERNAME` is set on
every Windows machine. A bare `__ENV.USERNAME` logs in as whoever is at the keyboard, every login
fails, and the summary reports a 100% error rate that looks like a platform fault. Prefix anything
whose bare name a shell might already own.

## What a mixed run looks like today

5 minutes, `RATE=2`, 20 kitchens, 8 drivers, compose stack with the generator co-located, empty
warm caches. Not a capacity measurement — the Milestone C gate:

| | |
|---|---|
| requests | 8,465 (25/s) · `http_req_failed` **0.00%** · 24,528 checks, **all passing** |
| journey | p95 **30 ms** · p90 21 ms · median 8 ms |
| `POST /orders` | p95 **75 ms** · 123 orders placed, 0 failures |
| login (`{scope:auth}`) | p95 3.7 s — the startup burst described above, not steady state |
| lifecycle | 373 kitchen transitions at 100% · 86 offers claimed · **84 deliveries completed** |
| platform agrees | 71 orders `Delivered`, 84 deliveries `Delivered` in the same window |

The gap between 123 orders placed and 84 delivered is the run's tail, not a fault: orders placed in
the last minute are still in flight when it stops. 35 deliveries ended `Unassigned` — see finding 2
above; that number is a direct function of `DRIVER_VUS` against `RATE`.

## Not yet built

| | |
|---|---|
| profiles (baseline, ramp, spike, soak), the runbook | Milestone D |
| Prometheus remote write, the `fds-load` Grafana dashboard, `handleSummary()` | Milestone E (which also replaces the deprecated `--summary-export` the runners use today) |
