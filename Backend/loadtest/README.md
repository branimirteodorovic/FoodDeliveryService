# Load testing

The k6 harness for **Feature 3.5 — Load Testing & Scalability Demonstration**
(`../LOADTESTING_PHASE3_PLAN.md`), and the runbook for using it. It covers **Milestone A** (the
foundation and the smoke test), **Milestone B** (the deterministic seed fixture), **Milestone C** (the
journey scripts), **Milestone D** (the load profiles, the breaking-point method, and what to watch
while a run is in flight) and **Milestone E** (the run in Grafana while it happens, and on disk
afterwards).

If you are here to run a test rather than to read about one, the order is:
[seed](#seed-the-dataset-first) → [check the environment](#before-every-run) →
[pick a profile](#the-four-profiles) → [read the result](#reading-a-ramp) →
[keep the evidence](#where-a-runs-results-go).

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
| `./run.sh scenarios/mixed.js --profile ramp` | Runs a [load profile](#the-four-profiles) — the shape of the run |
| `./run.sh scenarios/mixed.js --profile ramp --prometheus` | Also [streams the run into Grafana](#where-a-runs-results-go) and captures the platform's series afterwards |
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

## The four profiles

A profile is the *shape* of a run: how much load, for how long, climbing how fast, and what counts as
a pass. They live in `config/profiles.js` as **data**, which is the whole point — adding a test type
is a config entry, not a new script, and two runs of the same profile are comparable by construction.

| `--profile` | Shape (at the default `RATE=2`) | Wall clock | The question it answers |
|---|---|---|---|
| `baseline` *(default)* | 2/s, constant | 5 min | What does an unloaded system cost per request? **Everything else is read against this.** |
| `ramp` | 2 → 32/s in 8 steps, 90 s each | ~15 min | **Where is the knee?** The number this whole feature exists to produce. |
| `spike` | 2/s → 20/s for 60 s → 2/s | ~8 min | Does it recover, and how long does the queue take to drain? |
| `soak` | 4/s, constant | 2 h | Do memory, connections or the outbox backlog grow without bound? |

```bash
./run.sh scenarios/mixed.js --profile baseline
```

```bash
./run.sh scenarios/mixed.js --profile ramp
```

```bash
./run.sh scenarios/mixed.js --profile spike
```

Every profile is expressed as multiples of one number, so `-e RATE=` moves a whole profile up or down
without changing its shape — which is exactly what a bigger machine needs:

```bash
./run.sh scenarios/mixed.js --profile ramp -- -e RATE=5
```

| Knob | Default | |
|---|---|---|
| `RATE` | `2` | customer arrivals per second at 1× |
| `RAMP_STEPS` | `1,2,4,6,8,10,13,16` | the ramp's multipliers — chosen to span the [measured knee](#where-the-knee-is), with runway past it; narrow it to bisect |
| `RAMP_HOLD` | `90s` | per step. **60 s is a hard floor** — the outbox ticks every 5 s and a shorter step measures a transient |
| `WARM_HOLD` | `60s` | the ungated first phase of a staged run (below) |
| `SOAK_DURATION` | `2h` | |
| `DRIVER_VUS` / `KITCHEN_VUS` | sized from the profile's peak | `0` drops that scenario entirely — see *the fulfilment ceiling* |

The profile decides the executors, the stages, the duration, the driver pool and the thresholds.
`scenarios/mixed.js` decides the *mix* — which journeys, in what proportion (browse 70% / order 20% /
track 8%), with what supply behind them. A profile applied to a single journey script does nothing,
and that script says so in its own output rather than producing a flat run named after a ramp.

### Phases, and why the first minute is not gated

A staged profile (`ramp`, `spike`) tags every request with the phase it happened in and declares a
threshold **per phase** — journey p95, error rate and login p95, each with its own budget. That is
what makes the plan's saturation rule mechanical instead of a debate about a graph: the first phase
whose `p(95)` line goes red *is* the knee, and it is printed in the terminal with no Prometheus
involved.

The first phase is always `warm`: 60 s at the baseline rate, gated at nothing that can realistically
fail. It is there because the first spike run failed on the wrong thing — its `pre` phase, two minutes
at the *baseline* rate, recorded journey p95 **938 ms** with a 14.9 s maximum, while the 10× peak that
followed it managed 194 ms. No property of the platform explains that ordering. What `pre` had
measured was the run's own ignition: k6 building VUs, forty-odd operator VUs acquiring PBKDF2 tokens
inside a second or two, and every fixture identity's first authenticated request paying for a cold
Redis permission cache behind `CustomClaimsTransformation`. On a ramp the same transient would have
put the first red step at step one, every time.

`warm` is measured and printed like every other phase — it is real load and its numbers are worth
reading, that first-login cost being Milestone F #4's evidence. It is simply not allowed to be the
phase a knee gets attributed to. With it in place, the spike's `pre` phase reproduces the baseline
number to within 6% (32.6 ms against 31.0 ms), which is the check that says the warm-up is long enough.

## Before every run

Four things, in this order. All of them have burned a run in this repo.

**1. Is the stack up, and is anything else eating it?**

```bash
docker stats --no-stream
```

The generator, eight .NET services, Postgres, Redis, RabbitMQ, Jaeger, Seq and the collector share one
host. Anything above a few percent CPU before the run starts is coming out of the measurement.

The specific one that will get you: **Jaeger's `all-in-one` keeps spans in memory**. Measured here,
after a few profile runs it held 3.5 GB of the host's 7.6 GB and 2.2 of its 8 cores, and the next run
recorded journey p95 **6.1 s** where the same profile had measured 31 ms an hour earlier — nothing
about the platform had changed, the tracing backend had eaten the machine. `docker-compose.yml` now
caps it (`MEMORY_MAX_TRACES=20000`); if you are running against a stack that predates that, recreate
it before trusting a number:

```bash
docker compose up -d fooddeliveryservice.jaeger
```

**2. Is the fixture still valid?** Ids are per-database, and `docker compose down -v` invalidates all
of them:

```bash
dotnet run --project tools/FoodDeliveryService.LoadTest.Seeder -- --verify
```

**3. Is the queue empty?** A backlog left by the previous run is load this run did not offer and will
be blamed for:

```bash
docker exec FoodDeliveryService.Queue rabbitmqctl list_queues name messages
```

**4. Has anything touched `docker-compose.yml` or rebuilt an image since the last run?** If so, start
the generator's dependencies *before* the run rather than letting the runner do it:

```bash
docker compose up -d fooddeliveryservice.gateway fooddeliveryservice.identity
```

`docker compose run` starts the k6 service's `depends_on` — Gateway and Identity — **transitively,
and recreates any of them compose thinks is out of date**. Two ways that ruins a run, both met during
Milestone F:

- A run started 30 seconds after an unrelated `docker compose build` died in `setup()` with
  `dial tcp 172.18.0.7:8080: connect: connection refused`, because compose had just replaced the
  Identity container the driver roster was logging into. Nothing was wrong with the platform and
  nothing in the k6 output says so.
- Identity `depends_on` the **database**, so an edited `docker-compose.yml` gets a *recreated
  Postgres* as a side effect of starting the load generator. That is how a `max_connections` change
  landed silently in the middle of a controlled before/after — which is the one thing a bottleneck
  hunt cannot survive.

Doing it yourself first also means the run does not measure a cold Duende: the first token against one
costs ~7.9 s against 15–40 ms warm, and `smoke.js` is the cheap way to burn that off before a profile
starts. Run it twice if containers were just recreated — the first one is the warm-up.

Then record the environment next to whatever number the run produces. Without it the number is an
anecdote:

```
compose · 8 vCPU / 7.6 GB to Docker · generator co-located · 1 replica per service
services up: gateway identity users orders restaurants delivery notifications + postgres redis
             rabbitmq seq jaeger otel-collector
fixture: 20 restaurants × 24 items · 500 customers · 50 drivers
```

## The breaking-point method

The part that makes a result defensible. Follow it in order; skipping step 1 is what turns a capacity
number into an anecdote.

1. **Fix the environment and record it with the run** — compose or KinD, replica count, host CPU/RAM,
   whether the generator is co-located, which services are actually up. Use the block above.
2. **Ramp the arrival rate in steps**, each held long enough for the caches and the outbox to reach
   steady state — **at least 60 s**, given a 5 s outbox tick. `--profile ramp` does this; `RAMP_HOLD`
   is the knob and 90 s is the default.
3. **Declare saturation at the first step where** journey p95 exceeds the SLO **or** `http_req_failed`
   exceeds 1% **or** a queue/backlog metric grows monotonically across the whole step. The first two
   are per-phase thresholds in the summary. The third is not something k6 can see — it comes from the
   platform, and *What to watch while it runs* below is where to look.
4. **Identify the saturated component from the platform's own telemetry, not from k6.** k6 says *that*
   it slowed down; only the platform says *where*. RED per service
   (`app_request_duration_seconds` p95), `cache_hits_total` against misses,
   `delivery_assignment_outcome_total{outcome="lock_contended"}`, RabbitMQ queue depth, the
   `outbox_messages` backlog, Postgres connection count.
5. **Record the number, the component, and one Jaeger trace of a slow request at that step.** The
   trace is the artifact that makes the claim checkable — and it is why Jaeger's retention has to
   survive the run.

Then, and only then, change **one** thing and re-run the *same* profile in the *same* environment.
That is Milestone F's method, and this is the run it compares against.

## Reading a ramp

```
http_req_duration{scope:journey,phase:s04}
✓ 'p(95)<500' p(95)=180.11ms
http_req_duration{scope:journey,phase:s05}
✗ 'p(95)<500' p(95)=642.03ms
```

The knee is between `s04` and `s05`; the phase timetable printed at the top of the run says what
arrival rate each of those was. Four things to check before quoting it:

- **A phase with no data passes trivially.** An empty sub-metric has `p(95)=0`, so a run that aborted
  at `s05` shows `s06`–`s08` green with nothing behind them. Read the sample count next to the line,
  not just the tick.
- **`dropped_iterations` is the generator, not the platform** — k6 could not find a free VU in time.
  A few are normal at a step change; a rising count through a ramp means the profile out-ran the VU
  allocation and the offered load is no longer the load in the profile.
- **The run-wide thresholds are a stop condition, not the measurement.** `ramp` sets `abortOnFail` on
  the cumulative error rate and journey p95, which mixes every step so far and therefore trips some
  way *past* the step that broke. Deliberate division of labour: per-phase finds the knee, cumulative
  ends the run.
- **The step tag covers the hold, not the climb.** Traffic during a ramp-in is tagged `phase:tr` and
  gated by nothing, so each step's numbers describe a plateau rather than a plateau plus the climb
  onto it.

A spike reads the same way, with named phases instead of numbered ones — `warm`, `pre`, `peak`,
`post` — and the pass condition is **`post`**, not `peak`. Anything can survive sixty seconds; the
question is whether the queue drained afterwards.

## The capacity guardrail

Since **Milestone G** the Gateway has admission control — a global concurrency limit plus a
per-client fixed window, shaped by route tier — so past the knee it refuses a fraction of traffic
with `429` + `Retry-After` instead of accepting everything and timing out. The design, the numbers
and why the counters live in Redis are in [`../docs/rate-limiting.md`](../docs/rate-limiting.md);
what matters *here* is how a run reports it.

**A `429` is an answer, not a failure.** It is excluded from `http_req_failed` and from the status
check, and recorded in **`requests_throttled`** instead. If the harness counted it as an error, the
guardrail would fail the very test that motivated it and every run past the knee would report a
broken platform rather than a shedding one. The check keeps its original name so a before/after pair
of summaries stays comparable line for line.

So a ramp now has three numbers per step instead of two. The top two steps of the two published
Milestone G runs, same profile, same machine, the limiter the only difference:

```
without the limiter (g-before-01)
  s07  11:30–13:00   26/s  p95 538.71 ms   errors  0.26%   shed 0.00%   n=14,099
  s08  13:15–14:45   32/s  p95  14.39 s    errors 32.38%   shed 0.00%   n= 1,968

with it (g-after-01)
  s07  11:30–13:00   26/s  p95 566.10 ms   errors  0.39%   shed 3.27%   n=14,292
  s08  13:15–14:45   32/s  p95 554.42 ms   errors  0.35%   shed 4.99%   n=17,060
```

Read them together, and read `n` first. **The step where `shed` leaves zero is where the platform ran
out of capacity; the p95 beside it is what the requests that *were* admitted still got; and `n` is
whether the platform was still doing any work.** Without the limiter, s08 served a seventh of what
s07 served while the offered load rose by a quarter — throughput going *down* as load goes up is the
definition of the cliff. With it, s08 served more than s07 at the same latency, having refused one
request in twenty.

Three ways to read it wrong:

- **`shed` above zero on `baseline` or `smoke.js` is a mis-sized guardrail, not a busy platform.**
  Those profiles gate it at `rate<0.001` for exactly that reason — a limiter sized correctly is
  invisible below the knee. Fix the limit, do not loosen the threshold.
- **`shed` near 100% at a step is the opposite failure.** The limiter is refusing work the platform
  could have done. The staged profiles cap it at 50% per step (90% for a spike's `peak`) so this
  fails a run rather than reading as success.
- **Latency improving while `shed` climbs is not a win.** A refused request costs less than a served
  one, so shedding always flatters a percentile. Quote the shed fraction next to any p95 taken from a
  step where it was non-zero, or the number is a claim about traffic that was never served.

The one client in this harness that legitimately behaves like an abusive one is `driver.js`: it polls
the *administrator's* delivery board, so the whole driver pool shares one subject and one per-client
bucket. `ReadPermitLimit` is deliberately set above what that costs — see the offer-board workaround
above. If you raise `DRIVER_VUS` a long way, check whether the dispatch scope is being shed before
concluding anything about the platform.

To measure without the guardrail — which is how the published before/after was produced — use the
compose variable and recreate **only** the Gateway, which has no `depends_on` and so cannot drag a
fresh Postgres in behind it:

```bash
RATE_LIMITING_ENABLED=false docker compose up -d fooddeliveryservice.gateway
```

Export it for the `run.sh` invocation too, or `docker compose run` will decide the Gateway is out of
date and recreate it mid-run. Turning it *off* is the only honest way to measure "without": raising
the limits until they never bind leaves the middleware in the pipeline and is a different run. The
startup log says which mode it came up in — check it before trusting the numbers.

## Where a run's results go

Two destinations, answering different questions. Both are Milestone E.

### Live: Grafana, next to the platform's own metrics

```bash
./run.sh scenarios/mixed.js --profile ramp --prometheus
```

k6 streams into the **existing** Prometheus (Feature 2.4) over remote write, and the provisioned
`fds-load` dashboard draws it next to the services' own numbers: <http://localhost:3100/d/fds-load>.
The `Run` variable at the top filters to one `testid`, so a baseline and the ramp that followed it
can be compared while both are still in the retention window.

The panel worth the whole dashboard is **client p95 against server p95**. The client line contains
everything in front of the application — accept queues, the thread pool, the network; the server line
is what the hosts measured about themselves. While they track each other, the time is being spent
inside handlers and a cache or an index can move it. When they separate, the request is *waiting to
be served*, and the answer is admission control or another replica.

Three things to know before reading it:

- **It is off by default, and that is a measurement decision.** Streaming means Prometheus and Grafana
  are up and competing for the same host cores as the system under test — the reference numbers in
  this file were all measured with both stopped. Turn it on when the question is *where*, leave it off
  when the question is *how fast*, and never compare numbers across the two.
- **It expires.** Prometheus keeps **7 days**, on a volume `docker-compose.yml` calls disposable.
- **The `url` label does not exist.** `config/output.js` restricts k6's system tags to a bounded set,
  so an endpoint is identified by its route template. Without that, one ramp writes a time series per
  restaurant id into the platform's own metrics store.

`--prometheus` also, after the run, pulls the server-side series the dashboard draws out of Prometheus
into `results/…platform.json`, because that half of the evidence otherwise expires with the graphs.
Grafana **PNG** export is not automated: it needs the `grafana-image-renderer` plugin, which this
stack deliberately does not install. Use Grafana's own share menu and put the images in
`docs/assets/loadtest/`.

### Durable: `results/`, written at the moment the numbers exist

Every run — streamed or not — writes three files named `{script}-{profile}-{runId}`:

| File | What it is |
|---|---|
| `.summary.json` | k6's full summary object: every metric, every sub-metric, every threshold |
| `.summary.md` | the same run as a table — the thing that gets pasted into a PR or `docs/load-testing.md` |
| `.platform.json` | the server-side Prometheus series for the run's window (`--prometheus` only) |

`results/` is gitignored apart from `results/published/`, which holds the specific runs the
repository's documentation quotes. A published number with no artifact behind it is an assertion.

**`handleSummary()` replaces k6's default end-of-test summary**, and that is the point rather than a
side effect: the default prints every metric k6 holds, alphabetically, and buries the four lines that
decide whether a run is usable. What is printed instead is the traffic, the journey percentiles, the
business counters, the per-phase table and every threshold with its measured value — in the format
[*Reading a ramp*](#reading-a-ramp) above describes. The deprecated `--summary-export` is gone with it.

One thing the summary cannot record, and the markdown says so in its own header: **the environment**.
Host CPU/RAM, replica count and whether the generator was co-located are invisible to k6, and they are
half of what a capacity number means. Write them down next to it — [*Before every
run*](#before-every-run) has the block.

## What to watch while it runs

k6's terminal tells you *that* something slowed down. These tell you *where*. Everything except the
first row needs nothing but the stack itself.

| Where | What | Meaning |
|---|---|---|
| Grafana (`:3100`), `fds-load` — **only with `--prometheus`** | the run and the platform's response to it, on one screen | start here; the client-against-server panel says whether the wait is inside the application or in front of it |
| Seq (`:8081`), `CorrelationId like 'loadtest-<run-id>-%'` | one synthetic request's whole fan-out, including the legs that happen seconds later in another service | the single most useful view during a bottleneck hunt |
| `docker stats` | which container is at its ceiling | Identity pinned at a trivial request rate is Milestone F #6; Postgres pinned is #1 |
| `rabbitmqctl list_queues name messages` | queue depth climbing | the event pipeline is behind — the `_error` queues are the ones to look at first |
| `SELECT count(*) FROM outbox_messages WHERE processed_on_utc IS NULL` per service database | backlog growing monotonically across a whole step | **Milestone F #2**, the predicted real ceiling: `MessageProcessor` moves ~4 events/s per module |
| `SELECT count(*) FROM pg_stat_activity` | approaching 100 | Milestone F #1 — the default `max_connections` against six hosts with unbounded pools |
| Jaeger (`:16686`) | one slow trace from the failing step | step 5 of the method |

## What good looks like

Measured on the reference environment below, `--profile baseline`, twice, back to back:

| | baseline-01 | baseline-02 | agreement |
|---|---|---|---|
| journey p95 | 31.26 ms | 30.69 ms | **1.8%** |
| journey p99 | 77.32 ms | 75.97 ms | 1.7% |
| `POST /orders` p95 | 59.22 ms | 59.46 ms | 0.4% |
| requests | 8,211 (24.4/s) | 7,989 (23.7/s) | 2.7% |
| `http_req_failed` | 0.00% | 0.01% | |
| checks | 24,037, 100% | 23,369, 99.99% | |
| orders placed | 121 | 131 | 8% |
| deliveries completed | 113 | 103 | 9% |
| login p95 | 2.92 s | 2.04 s | 30% |

> compose · 8 vCPU / 7.6 GB to Docker · generator co-located · 1 replica per service · Notifications,
> RealTime, FraudDetection, Prometheus and Grafana not running · fixture 20 restaurants × 24 items,
> 500 customers, 50 drivers

**The tolerance, stated so a future run can be called noise or not:**

| | Agree within | Why |
|---|---|---|
| journey p95/p99, `POST /orders` p95 | **±5%** | the run's headline numbers; anything wider and the harness is measuring the host |
| request throughput | ±5% | |
| orders placed, deliveries completed | ±10% | ~120 samples per run — Poisson noise alone is several percent |
| login p95 | **not comparable** | dominated by the ignition burst, which is a function of VU count and host scheduling, not of the platform. Read it per phase on a staged profile instead |

If two consecutive baselines fall outside that, stop: the harness is measuring noise and nothing after
this milestone is trustworthy. Start with `docker stats` — it has been the answer both times so far.

A cross-check that costs nothing: the `spike` profile's `pre` phase runs at the baseline rate, and it
measured **32.61 ms** against the baseline's 31.26/30.69 ms. Two different profiles, an hour apart,
agreeing to within 6%.

## Where the knee is

Two runs found it, and the first one is here because how it *failed to* is the more instructive half.

`--profile ramp` with `RAMP_STEPS=1,2,3,4,5,6,8,10` — the original default — on the reference
environment: eight steps of 90 s, 1,633 orders placed, 69,336 requests over fifteen minutes.

| Step | Customers/s | Requests/s (approx) | journey p95 | errors |
|---|---|---|---|---|
| `warm` | 2 | | 75.25 ms | 0.00% |
| `s01` | 2 | 25 | 56.76 ms | 0.00% |
| `s02` | 4 | 50 | 37.50 ms | 0.00% |
| `s03` | 6 | 75 | 44.79 ms | 0.00% |
| `s04` | 8 | 100 | 101.52 ms | 0.00% |
| `s05` | 10 | 125 | 212.22 ms | 0.02% |
| `s06` | 12 | 150 | 77.36 ms | 0.00% |
| `s07` | 16 | 200 | 313.08 ms | 0.04% |
| `s08` | 20 | 250 | **282.55 ms** | 0.08% |

Every step passed — 20 customers/s sits at 56% of the journey SLO with an error rate two orders of
magnitude under the gate. What the curve shows is a *bend*: p95 grows 5× while the offered rate grows
10×, and an error rate begins to exist at all. That is the approach to saturation, not saturation.

Note `s06`, at 77 ms between `s05`'s 212 ms and `s07`'s 313 ms. A single non-monotonic step is host
scheduling and GC, not a discovery. **Two adjacent steps make a trend; one does not** — which is why
the method bisects with `RAMP_STEPS` instead of quoting a step.

So the range was extended, starting from the last step of the previous run so the two tables join:

```bash
./run.sh scenarios/mixed.js --profile ramp -- -e RAMP_STEPS=10,13,16,20,25
```

| Step | Customers/s | journey p95 | errors | `POST /orders` p95 | placement failures |
|---|---|---|---|---|---|
| `s01` | 20 | 229.95 ms | 0.00% | | |
| `s02` | **26** | **1.85 s** | **3.83%** | 4.89 s | 10.02% |

**The knee is between 20 and 26 customers/s** — roughly 250 to 320 requests per second — on the
reference environment. `s02` crosses both saturation criteria at once, the run aborted there instead
of spending six more minutes recording a collapse, and `s03`–`s05` are in the summary with `p(95)=0`
and no samples behind them: the trivially-passing empty phase, in the wild.

### Where it is now

Both tables above predate Milestone F. The Milestone G before-run (`g-before-01`, the stock
`1,2,4,6,8,10,13,16` ramp, limiter off) re-measured the same environment after F's fixes:

| Step | Customers/s | journey p95 | errors | served |
|---|---|---|---|---|
| `s06` | 20 | 477.72 ms | 0.21% | 11,441 |
| `s07` | 26 | 538.71 ms | 0.26% | 14,099 |
| `s08` | 32 | **14.39 s** | **32.38%** | **1,968** |

So the knee moved out by roughly one step — 26 customers/s now sits just over the SLO rather than
collapsing through it — and the collapse moved to 32. Treat the shift as indicative rather than
measured: the ramp *shape* differs from the two runs above (different steps, different warm-up
history), so this is not the one-variable comparison the `f-*` and `g-*` pairs are.

What is not indicative is `s08`. Throughput falling to a seventh while offered load rises is the
cliff itself, and it is what Milestone G's guardrail was built to replace — see
[The capacity guardrail](#the-capacity-guardrail) and `../docs/load-testing.md` → *Round two*.

### What saturated

Step 4 of the method, from `docker stats` sampled every 15 s through the failing step (the platform's
own telemetry now has a dashboard — [*Where a run's results go*](#where-a-runs-results-go) — but this
is the version that needs nothing):

| | at 20/s | at 26/s, just before the abort |
|---|---|---|
| Identity | 100% | **228%** |
| Postgres | 52% | **187%** |
| Orders.Api | 66% | 130% |
| Delivery.Api | 64% | 66% |
| Gateway | 59% | 49% |
| k6 (the generator) | 72% | 56% |
| **total, of 800%** | ~460% | **~780%** |

The host ran out of cores, and **the single largest consumer at the knee was password hashing.**
Identity took 2.3 of the 8 cores to issue tokens while the entire application path — Gateway, Orders,
Restaurants, Delivery — used about 3. That is Milestone F #6 exactly, and it comes with a caveat that
has to travel with the number:

> **An arrival-rate ramp conflates "a customer arrives" with "a customer signs in."** Every new VU
> logs in once, so a rising arrival rate is also a rising rate of PBKDF2 verifications — the most
> expensive operation in the system by an order of magnitude. A real population is mostly returning
> users holding valid tokens. The knee above is therefore a *lower* bound on the platform's capacity,
> and closing that gap is a harness question (a warm token pool) before it is a platform one.

The generator itself stayed at 56–72% of a core throughout, so the knee is not k6 running out of road
— but it is one machine's eight cores shared between the load and the thing being loaded, and the
number does not transfer. Record the environment with it, every time.

**The default `RAMP_STEPS` now spans this**: `1,2,4,6,8,10,13,16` puts the knee at step six or seven
with runway either side, so a stock `--profile ramp` on this hardware finds it instead of reporting
that everything was fine. On a bigger machine, raise `RATE` or extend the steps — the cumulative
`abortOnFail` is what keeps the extra ones cheap when they turn out to be unnecessary.

## The fulfilment ceiling — a limit of the harness, not the platform

Above roughly **0.9 orders per second** — `RATE=4.5` at a 20% order share — deliveries begin to strand
in `Unassigned` no matter how many drivers run, and that is the harness's fault rather than the
platform's.

The cause is the offer-board workaround (see above): with no way for a driver to discover an offer
over REST, a pool of *P* drivers wastes about *P/2* claims per delivery, so a pool ticking every
~2.25 s completes about `(P / 2.25) / (P / 2 + 3)` deliveries per second — 0.51/s at 8 drivers,
0.71/s at 24, and **asymptotically 0.89/s however many drivers run**, because the waste grows with the
pool. `config/profiles.js` therefore caps the pool at 24 and `mixed.js` warns whenever a profile's
peak crosses the line.

Past it, read the placement and read-path numbers and treat the fulfilment ones as a floor — or take
the supply side out of the picture entirely and say so, which is the honest way to run a ramp whose
question is about the read path:

```bash
./run.sh scenarios/mixed.js --profile ramp -- -e KITCHEN_VUS=0 -e DRIVER_VUS=0
```

The real fix is a "my offers" read model or the SignalR push, in the Delivery feature.

## Soak hygiene

A 2-hour soak at `RATE=4` places on the order of **6,000 orders** and writes several times that many
outbox, inbox and audit rows across seven databases. Two consequences.

**Reset between soaks.** Everything the seeder creates is keyed on the `loadtest-` prefix (emails) and
`LOADTEST-nnnn` (restaurant tax ids), which is what makes cleanup possible at all — but the orders,
deliveries and outbox rows a *run* produces carry no such marker. The supported reset is the blunt one,
from `Backend/`:

```bash
docker compose down -v && docker compose up -d
```

then re-seed (~3 min) and re-verify. A soak started against a database that has already been soaked is
measuring table growth from the previous run as well as its own, which is the one thing a soak is
supposed to be able to attribute.

**Watch these three across the two hours**, because a soak that only reports latency has answered the
wrong question:

- unprocessed `outbox_messages` per service — flat is the pass, monotonic is Milestone F #2;
- each service's container memory (`docker stats`) — and Jaeger's, which is capped now but is the
  first thing to check if the host starts swapping;
- `pg_stat_activity` count — Milestone F #1 predicts this climbs toward the default `max_connections`
  of 100.

A soak is run **by hand, once**, and never by CI. It is also the only profile that will notice a leak,
so it is worth the two hours before quoting any capacity number as sustainable.

## Layout

```
loadtest/
├── config/
│   ├── environments.js   compose | compose-host | kind → gateway + identity URLs, credentials, run id
│   ├── output.js         handleSummary — the terminal report and the durable artifacts; the tag allow-list
│   ├── profiles.js       baseline | ramp | spike | soak — stages, phases, per-phase thresholds
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
└── results/              summary.json + summary.md + platform.json per run
                          gitignored except results/published/
```

The Grafana dashboard and the remote-write receiver live with the rest of the observability stack, not
here: `../docker/grafana/dashboards/load.json` and the Prometheus `command:` in `../docker-compose.yml`.
`Common.UnitTests/Observability/ObservabilityAssetTests` is what keeps them honest — it pins the
dashboard uid set and rejects any panel querying a metric nothing produces.

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
| `requests_throttled` | `rate<0.001` — the Gateway's guardrail must be invisible below the knee. Raised per profile; see [The capacity guardrail](#the-capacity-guardrail) |

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

On top of that, each profile applies its own overrides, and the staged ones replace the run-wide login
budget with one **per phase**:

| Profile | Journey p95 | Errors | Shed (429) | Login p95 | Aborts |
|---|---|---|---|---|---|
| `baseline`, `soak` | 500 ms, strict | 1% | 0.1%, strict | 4 s run-wide | no |
| `ramp` | 500 ms **per step** | 1% per step | 50% per step | 8 s per step | yes — cumulative p95 and error rate |
| `spike` | 500 ms in `pre`/`post`, 5 s in `peak` | 5% | 1% in `pre`/`post`, **90% in `peak`** | 4 s in `pre`/`post`, 15 s in `peak` | no |

The shed budgets are asymmetric on purpose. Flat profiles run below the knee, so any shedding there
means the limiter is mis-sized. Staged profiles are *supposed* to shed at the top — the budget is
there to catch the other failure, a limiter set so low it refuses work the platform could have done.
A spike's `peak` may shed almost everything; its `pre` and `post` may not, because a guardrail still
refusing traffic two minutes after the crowd left has not recovered.

`baseline` is strict partly *because it runs for five minutes*. Shortened with `-e DURATION=60s` it
fails on login alone — measured 5.57 s — because the ignition burst is then most of the run with
nothing to dilute it. Use the short form to check that a change did not break the harness, never to
produce a number.

The login budget is 4 s rather than the shared 2 s because a mixed run starts ~50 VUs and each one's
first iteration acquires a token, so Identity is handed dozens of PBKDF2 verifications inside a second
or two. Measured: p95 2.87 s, median 1.09 s, against 643 ms for smoke.js's five concurrent logins and
~150 ms for one. It is a **startup transient that scales with the VU count**, not the steady-state cost
of signing in — and it is budgeted rather than excluded because token issuance being the most expensive
endpoint in the system is a real capacity fact (Milestone F #6).

The staged profiles drop the run-wide login threshold entirely: a cumulative percentile on a staged run
is fixed by that same ignition burst and says nothing about any later phase, and a 20-second budget
nothing can cross is worse than no budget because it looks like one. They gate login per phase instead,
which is what lets a spike assert that the token endpoint *recovered* — measured at 10×: 2.81 s at the
peak, back to a phase with no new logins at all afterwards.

The shared numbers are a starting SLO — chosen, not measured — but they now have a measurement behind
them: the reference baseline runs at journey p95 **31 ms** against the 500 ms budget, so the gate has
an order of magnitude of headroom and will not fail on ordinary host noise. See
[what good looks like](#what-good-looks-like).

## Read this before quoting a number

**The generator shares the host with the system under test.** k6, eight .NET services, Postgres,
Redis, RabbitMQ, Prometheus, Grafana, Jaeger and Seq all run on one machine. Above roughly half the
host's cores, the results describe that contest and not the platform. Every published number has to
carry the environment it came from — host CPU/RAM, replica count, compose or KinD, generator
co-located or not. Milestone H's `docs/load-testing.md` is where that record lives.

This is not a theoretical caveat. The same profile measured journey p95 **31 ms** and, two hours
later on the same machine, **6.1 s** — because Jaeger's in-memory span store had grown to 3.5 GB and
2.2 cores in between. [*Before every run*](#before-every-run) exists because of that afternoon.

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
| a CI performance smoke job, and a generator that is not co-located | optional Milestones I and J |

Everything this harness has measured — the published numbers, the environment each came from, the
round-one and round-two before/afters and the fixes that were reverted — is in
[`../docs/load-testing.md`](../docs/load-testing.md) (Milestone H), which is also where the graphs in
the project [`README.md`](../../README.md) come from. Milestone G's guardrail design is in
[`../docs/rate-limiting.md`](../docs/rate-limiting.md).

`node scripts/plot.mjs` regenerates those graphs from `results/published/` alone — no stack, no
Grafana, no network. Prometheus keeps seven days on a disposable volume, so a published graph has to
be redrawable from an artifact that does not expire.
