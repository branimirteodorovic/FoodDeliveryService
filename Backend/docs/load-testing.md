# Load testing — the bottleneck log

> Started by **Feature 3.5 — Load Testing & Scalability Demonstration**
> (`LOADTESTING_PHASE3_PLAN.md`), **Milestone F**. Milestones A–E built the harness — how to seed,
> how to run each profile, what each threshold means and where a run's artifacts go all live in
> [`../loadtest/README.md`](../loadtest/README.md), which is the runbook and stays the runbook.
> **This document is the other half: what the harness found, what was changed because of it, and
> what was changed and then reverted.** Milestone H expands it into the reference write-up with the
> published numbers and the extrapolation; what is here now is the log of round one.

## Why a log of failures is the point

The method the plan fixes for this milestone is deliberately boring, and it is the whole value:

> run the profile, record, change **one** thing, re-run the *same* profile in the *same*
> environment, record. Anything that doesn't improve the number gets reverted, and gets a line
> saying it didn't help.

A list of fixes with no numbers beside them is a list of opinions. A negative result — *we thought it
was X, we measured, it wasn't* — is the most credible thing in a document like this, because it is
the thing nobody writes down unless they actually ran the test.

## The environment every number below came from

```
compose · 8 vCPU · 7.6 GB to Docker · generator co-located · 1 replica per service
up:   gateway identity users orders restaurants delivery notifications
      postgres redis rabbitmq seq jaeger otel-collector
down: realtime frauddetection prometheus grafana blackbox
fixture: 20 restaurants × 24 menu items · 500 customers · 50 drivers
k6:    grafana/k6:latest, inside the compose network (ENV=compose)
profile: ramp · RAMP_STEPS=10,13,16,20,25 of 2/s · 90 s per step
```

Two things about it that matter when reading any number here:

- **The generator shares the host with everything it measures.** Above roughly half the host's cores
  the results describe that contest. Every number below is therefore a *relative* claim — the same
  profile, the same machine, the same afternoon, one variable changed — and not a capacity figure
  that transfers anywhere.
- **A platform-side sampler ran during every run** (`pg_stat_activity`, per-database outbox/inbox
  backlog, `docker stats`, every ~12 s). It costs CPU. It ran identically for the before and the
  after, so it cancels out of the comparison and is left in rather than quietly dropped.

## What the ramp actually indicted

The plan listed seven predicted bottlenecks. Round one measured them; here is the scorecard before
any fix was made. Evidence is from one `ramp` run that reached 20 customers/s and aborted at 3:01.

| # | Predicted | Verdict | The evidence |
|---|---|---|---|
| 1 | Postgres connection exhaustion | **confirmed** | **678** × `Npgsql.PostgresException 53300: sorry, too many clients already` — Restaurants 256, Delivery 230, Orders 192. `pg_stat_activity` sampled 88 client backends of a `max_connections` of 100, **80 of them idle**. The plan predicted this one almost verbatim. |
| 2 | The event pipeline is the real ceiling | **confirmed** | Orders' unprocessed outbox went 9 → 318 rows across one 90 s step and drained afterwards at ~3.4 rows/s — the arithmetic of a 20-row batch every 5 s. Also, unindicted by the plan and found here: the dispatch query had **no index** and sequentially scanned the whole table. |
| 3 | `GetRestaurantsQuery` is the one uncached browse query | **confirmed here, overturned later** | In the same run, at near-identical call volume: `GetRestaurants` 970 calls at **242 ms** average, against `GetMenu` 929 at 30 ms and `GetRestaurant` 937 at 26 ms. That reading was contaminated by #1 and does not survive fixing it — see change 3 below, which is the entry worth reading. |
| 4 | Permission-resolution storm | **rejected** | `user_permissions` cache: 9,029 hits against 226 misses (**97.6%**), and Users averaged **1.9% CPU** through the whole run while five other services were at 25–60%. The 5-minute Redis TTL is doing its job; there is nothing here to fix. |
| 5 | Redis carrying four workloads | **not reached** | Redis averaged 5.7% CPU. It is still a single point of failure by design (`caching.md` §1), which is an availability fact, not a throughput one. |
| 6 | Token issuance is CPU-bound by design | **confirmed, no fix** | Identity averaged 40.7% CPU and its `warm`-phase login p95 was 9.4 s while the journey p95 beside it was 236 ms. This is PBKDF2 doing exactly what it is for. Recorded as a capacity fact. |
| 7 | Assignment lock contention | **not reached** | No `lock_contended` outcomes recorded. The run never got far enough past the other three to load it. |

## Round one, change by change

### 1. The event pipeline: an index, a faster tick, and `SKIP LOCKED`

**What the data said.** Two separate problems behind one symptom.

The first is not in the plan's list because reading the code does not reveal it — you have to look at a
query plan. `ProcessOutboxJob` and `ProcessInboxJob` select
`WHERE processed_on_utc IS NULL ORDER BY occurred_on_utc LIMIT n FOR UPDATE`, and **no index
supported that predicate on any of the eleven tables.** Rows are never deleted, so the cost of finding
the next batch grows with everything the module has ever published. On the Delivery outbox at 32,958
rows, 99.99% of them long processed:

| | Buffers read | Execution |
|---|---|---|
| Sequential scan (before) | 2,567 | **16.05 ms** |
| Partial index (after) | 1 | **0.125 ms** |

That runs twice per module every tick, forever, and it gets slower every day the system is used. The
index is partial on the same predicate, so it holds only unprocessed rows — 8 KB against a 22 MB
table, and an `UPDATE` that sets `processed_on_utc` removes the entry rather than rewriting it. The
index cannot become the thing that grows.

The second is the plan's #2, and the ramp showed it plainly: `MessageProcessor` was `IntervalInSeconds: 5`
with `BatchSize: 20` in every host — a hard ceiling of four events per second per module — and at 20
customers/s the unprocessed row count climbed from 9 to 367 across all five databases (318 of them
Orders') inside one 90 s step.

**What changed.** The partial index on all eleven outbox/inbox tables (one migration per module);
`IntervalInSeconds: 1` and `BatchSize: 50`; and `SKIP LOCKED` on the `FOR UPDATE`. The three belong
together: **a one-second tick is only affordable because the index made the probe free** — twelve jobs
sequentially scanning growing tables every second would have been strictly worse than what it
replaced.

**What it did.** Same profile, same machine, same afternoon:

| | before (`f-before-02`) | after (`f-pipeline-01`) |
|---|---|---|
| Unprocessed rows, peak | 367 | 453 |
| Drain rate | 2.96 rows/s | **9.44 rows/s** |
| Time to clear the backlog | 124 s | **48 s** |
| `http_req_failed` | 0.40% | **0.00%** |
| checks | 99.86% | **100.00%** |
| Order placement failures | 2.09% | **0.00%** |
| Orders placed | 185 | 209 |
| journey p95 | 746 ms | 789 ms |
| journey p95, `s01` (20/s) | 576 ms | 675 ms |

**Read the last two rows honestly: latency got slightly worse, and that is not a mystery.** The before
run was failing 0.4% of its requests and 2% of its order placements, and a request that fails costs
less than a request that succeeds. The after run completed more work — 209 orders against 185, zero
failures — on the same eight cores. Shedding load is not a latency optimisation, but it does flatter
one.

**About `SKIP LOCKED`: no effect was measured, and none was expected.** With one replica per service
Quartz's `[DisallowConcurrentExecution]` already serializes each job, so there is never a second
scheduler to skip past. It is in because `KUBERNETES_PHASE2_PLAN.md` §5.1 names `FOR UPDATE` without
it as one of three hazards blocking replicas > 1, and because it is one word next to a change that had
to touch those eleven queries anyway. It is recorded here as a scale-out prerequisite, not as a
result.

**One caveat, stated because leaving it out would make the table above a lie.** `docker compose run`
starts the k6 service's transitive dependencies, and Identity depends on the database — so the
Postgres container was recreated during this run's warm-up and picked up the `max_connections=200`
already sitting in `docker-compose.yml` for change 2. This run therefore measures the event-pipeline
change **plus** the server half of the connection fix, which is why `53300` went to zero here rather
than in the next section. The event-pipeline numbers (drain rate, backlog, tick cost) are unaffected;
the error-rate numbers belong to both changes. `loadtest/README.md` → *Before every run* now has the
step that prevents this.

### 2. Bounded Npgsql pools, and a server ceiling that matches them

**What the data said.** Nothing in any connection string set a pool bound, so every pool was Npgsql's
default of 100. Each module host builds **two** pools from its one connection string — the shared
`NpgsqlDataSource` used by Dapper and the outbox/inbox jobs, and EF Core's own — so seven hosts could
demand up to 1,400 connections from a `postgres:17` running its default `max_connections=100`. What
that produced, in one three-minute ramp: **678** × `Npgsql.PostgresException 53300: sorry, too many
clients already` (Restaurants 256, Delivery 230, Orders 192), and `pg_stat_activity` sampled at 88
backends of which **80 were idle**. The pools were hoarding connections they were not using and then
failing to get more.

**What changed.** `Maximum Pool Size=10` on every module host's connection string and `20` on
Identity's — a bounded worst case of 6 × 20 + 20 = 140 — and `max_connections=200` on the server so
that ceiling has slack for a psql session, the seeder or a job. Both compose and `deploy/k8s`, because
a limit that only exists in one environment is a limit nobody can rely on.

The `deploy/k8s` half was verified on a real KinD cluster rather than by reading the YAML: the pods
come up with `Maximum Pool Size=10` in `ConnectionStrings__Database`, Postgres reports
`max_connections = 200`, and all eleven dispatch indexes exist — including RealTime's inbox-only one,
which is the single path compose cannot exercise because it does not run that service.

**What it did.** Same profile, same machine; the previous run is the baseline:

| | before (`f-before-02`) | +pipeline (`f-pipeline-01`) | +bounded pools (`f-pools-01`) |
|---|---|---|---|
| journey p95 | 746 ms | 789 ms | **586 ms** |
| journey p95, `s01` (20/s) | 576 ms | 675 ms | **572 ms** |
| journey **p99** | 2.31 s | 2.03 s | **1.15 s** |
| `POST /orders` p95 | 1.43 s | 2.00 s | **919 ms** |
| `53300` errors | 678 | 0 | **0** |
| pool-exhaustion timeouts | 0 | 0 | **0** |
| Postgres backends, peak | 88 / 100 | 119 / 200 | **87 / 200** |
| Unprocessed rows, peak | 367 | 453 | **207**, cleared within 13 s |

Two things worth pulling out. **p99 halved and order placement more than halved** — tail latency is
where connection contention lives, and it is the half of the distribution a p95 was quietly hiding.
And **the bounded pools did not trade one error for another**: the failure a too-small pool produces
is `The connection pool has been exhausted`, and there were none, which says 10 is a bound the load
fits inside rather than a new wall.

The middle column is also the argument for doing both halves. With `max_connections` raised but pools
still unbounded, the pools simply grew into the new ceiling — 119 backends instead of 88 — and the
latency was the worst of the three runs. Raising a server limit in front of an unbounded client is
not a fix, it is a longer fuse.

### 3. Caching the browse list — written, measured, reverted

**This is the one that did not survive contact with the data, and it is the most useful entry here.**

The plan predicted it (#3) and the first pass of telemetry agreed emphatically. In the before run,
across a browse journey that calls list → detail → menu exactly once each:

| | calls | average |
|---|---|---|
| `GetRestaurantsQuery` (uncached) | 970 | **242.4 ms** |
| `GetMenuQuery` (cached) | 929 | 29.7 ms |
| `GetRestaurantQuery` (cached) | 937 | 26.4 ms |

Near-identical volume, an 8–9× cost difference, and one obvious explanation: it is the only browse
read that reaches Postgres. `GetRestaurantsQuery` was duly made an `ICachedQuery`, with a 30-second
TTL rather than the entity keys' five minutes because a paged `ORDER BY name` key cannot be evicted
exactly.

Then change 2 landed, and the same three reads were measured again with nothing else different:

| | calls | average |
|---|---|---|
| `GetRestaurantsQuery` (uncached) | 976 | **26.3 ms** |
| `GetMenuQuery` (cached) | 902 | 24.2 ms |
| `GetRestaurantQuery` (cached) | 940 | 23.4 ms |

The 9× gap was **12%**. The 242 ms was never the cost of a missing cache — it was the cost of being
the one Restaurants read holding a Postgres connection while the pool starved, and it disappeared when
the pool stopped starving. What the cache would actually buy is ~2.9 ms on ~1,000 calls: under 3
seconds of handler time across a three-minute run, on a host whose two repeat baselines agree only to
within 1.8% of journey p95. **A ramp cannot measure that**, and running one to produce a number
indistinguishable from noise would be theatre, not evidence.

What it would cost is not noise: a cached surface with no exact eviction, on the entry point of every
browse. So the change was reverted — `RestaurantCacheKeys.ListExpiration`, the `ICachedQuery`
implementation and its unit test are all gone, and `caching.md` now records *why* `GetRestaurants`
stays uncached with the measurement instead of the original guess about key permutations.

**The general lesson, which is the reason to write this down:** the first bottleneck in a queue makes
every component behind it look guilty. Measuring #3 before fixing #1 would have shipped a cache, seen
the latency collapse, and credited the wrong change — with a plausible story and a graph to match.
Fix the wall in front before you believe anything about what is behind it.

## What actually saturates first, on this environment

Worth stating before anyone reads the table above as "these fixes were disappointing". The sampler
totalled every container's CPU during each run:

| | peak total container CPU |
|---|---|
| before (`f-before-02`) | **795%** of 800% |
| after the event pipeline (`f-pipeline-01`) | **830%** of 800% |
| after bounding the pools (`f-pools-01`) | **942%** of 800% |

Eight cores, and at the 20 customers/s step they are gone — to eight .NET services, Postgres, Redis,
RabbitMQ, Jaeger, Seq, the collector **and the load generator, which is on the same host**. (The
totals above 800% are `docker stats` sampling containers at slightly different instants, not spare
capacity; the point they make is the same either way.) No fix to any single component moves a number
in that state; it can only change how the saturated CPU is spent. That is what the three changes
above do, and it is why they show up as *errors removed* and *work completed* rather than as latency
won.

The plan said this would happen — *"above roughly half the host's cores the results describe the
contest, not the platform"* — and the honest form of the round-one result is therefore: **the knee on
this environment is host CPU at roughly 20 customers/s, and the component-level ceilings found
underneath it are the ones that would bind next on hardware that is not.** Milestone J (a generator
that is not co-located) is what turns that into a platform number.

## Did the saturation point move?

The runbook's breaking-point method declares saturation at the first step where **any** of three
things happens. Taking them one at a time at the 20 customers/s step, which is where all three runs
ended:

| Criterion | before | after round one |
|---|---|---|
| journey p95 over the 500 ms SLO | 576 ms — **saturated** | 572 ms — **still saturated** |
| `http_req_failed` over 1% | 0.40% — not tripped | 0.17% — not tripped |
| a backlog growing monotonically across the step | 9 → 367 rows, 124 s to clear — **saturated** | peak 207, cleared inside 13 s — **not saturated** |

So: **two of the three criteria tripped before round one and one does now.** The
event pipeline no longer falls behind the API in front of it, and connection exhaustion — which was
producing 678 hard failures a run — is gone entirely. The latency criterion did not move, and the
section above says why it cannot on this hardware: the eight cores are already fully consumed at that
step, by the system *and its own load generator*.

That is the honest state of the knee after round one. Moving the latency criterion needs either an
environment where the generator is not competing for the cores it measures (Milestone J), or
admission control so the platform sheds instead of queueing past it (Milestone G) — not another
component fix.

## Round two: admission control, and what it can and cannot move

**Milestone G is built** — the Gateway now has a global concurrency limit plus a per-client fixed
window, shaped by route tier, with counters in Redis. The design, the defaults and their arithmetic
are in [`rate-limiting.md`](rate-limiting.md); this section is about what it means for the numbers
above.

Two things are worth stating before any before/after is quoted, because they are true regardless of
what that run measures:

- **A limiter does not add capacity.** The section above is unambiguous that the ceiling on this
  environment is the eight cores, consumed by the platform *and its own generator*. Shedding cannot
  create a core. What it changes is how a saturated system spends what it has: a fixed fraction of
  requests refused in microseconds, the rest served, instead of all of them queued and most of them
  abandoned.
- **Latency improving because traffic was shed is not a latency improvement.** A refused request
  costs less than a served one, which is the same effect round one already ran into in the opposite
  direction (change 1: latency got *worse* because the after run stopped failing 2% of its orders and
  therefore completed more work). Any p95 taken from a step with a non-zero shed fraction has to be
  quoted next to that fraction, or it is a claim about traffic that was never served.

So the criterion the guardrail was expected to move is the third one — a queue growing without
bound — and the shape of the result, not its magnitude: **a plateau where there used to be a cliff,
with the refused share stated rather than expressed as timeouts.**

That is what it did.

### The run

The stock eight-step ramp — 2 → 32 customers/s, 90 s per step — rather than the `f-*` runs'
`RAMP_STEPS=10,13,16,20,25`, because the question changed: those were bisecting a known knee, these
have to show both sides of it. Same machine, same afternoon, same fixture, **one variable**:

```
before  g-before-01   RateLimiting__Enabled=false   the pre-Milestone-G gateway
after   g-after-01    RateLimiting__Enabled=true    global concurrency 48, per-client 200/60/300 per 10 s
```

### Per step — the cliff and the plateau

| step | rate | before p95 | before errors | before served | after p95 | after errors | **after shed** | after served |
|---|---|---|---|---|---|---|---|---|
| s05 | 16/s | 322 ms | 0.30% | 9,582 | 306 ms | 0.00% | 0.00% | 9,703 |
| s06 | 20/s | 478 ms | 0.21% | 11,441 | 547 ms | 0.02% | 0.41% | 11,375 |
| s07 | 26/s | 539 ms | 0.26% | 14,099 | 566 ms | 0.39% | 3.27% | 14,292 |
| s08 | 32/s | **14.39 s** | **32.38%** | **1,968** | **554 ms** | **0.35%** | **4.99%** | **17,060** |

**Read the last column first.** At 32 customers/s the unguarded platform served **1,968** requests in
the step — a seventh of what it had served at 26/s, while the offered load had *risen* by a quarter.
That is the cliff, and it is the definitional one: throughput went down as load went up. With the
limiter, the same step served **17,060** — 8.7× more — at a p95 of 554 ms, which is the same latency
it was delivering two steps earlier.

The price was **4.99% of requests refused**. That is the entire trade: shed one request in twenty and
the other nineteen are served in half a second; shed none and two thirds of them fail after fourteen.

### Run-wide

| | before | after |
|---|---|---|
| Requests completed | 77,881 (92.8/s) | **95,908 (104.0/s)** |
| `http_req_failed` | 1.06% | **0.20%** |
| checks | 99.65% | **99.94%** |
| journey p95 | 438 ms | 476 ms |
| journey **p99** | 1.09 s | **749 ms** |
| journey **max** | 23.37 s | **5.76 s** |
| `POST /orders` p95 | 1.06 s | **782 ms** |
| **Order placement failures** | **4.02%** | **0.00%** |
| Orders placed | 1,846 | **2,428** |
| Kitchen transitions | 4,453 | **6,878** |
| Dropped iterations | 642 | **393** |
| Requests shed | 0 | 1,581 (1.66%) |
| Peak total container CPU | 832% of 800% | 867% of 800% |

**The p95 got 38 ms worse, and that is not a defect — it is the same effect change 1 above ran into.**
The after run completed 18,000 more requests and 582 more orders on the same eight cores. A refused
request is cheap and a *failed* one is cheaper still, so the before run's percentile is flattered by
the third of s08 it never served. p99, max, the order path and the error rate all move the right way,
and those are the numbers a customer actually experiences.

### Was the shedding shaped, or just shedding?

The whole argument for route tiers is that a `429` on a browse is cheap and a `429` on a delivery is
not. Every one of the 1,581 rejections, taken from the Gateway's own request log:

| Route | Tier | Shed |
|---|---|---|
| `GET /delivery/orders/{id}/delivery` | Read | 282 |
| `GET /orders/{id}` | Read | 275 |
| `GET /restaurants/{id}` | Read | 244 |
| `GET /restaurants/{id}/menu` | Read | 240 |
| `GET /restaurants` | Read | 240 |
| `GET /delivery/deliveries` | Read | 91 |
| `POST /delivery/drivers/me/location` | Write | 75 |
| `POST /orders` | Write | 64 |
| `GET /orders` | Read | 56 |
| **any order or delivery lifecycle transition** | **Critical** | **0** |
| **`/health/*`, `hubs/**`** | **Exempt** | **0** |

1,428 reads (90%), 153 writes (10%), **zero lifecycle transitions and zero health probes**. Not one
`accept`, `ready`, `picked-up` or `delivered` was refused across a run in which the platform was over
its capacity for three minutes. The ranking did exactly what it was written to do, and the
`0.00%` placement-failure row above is the same fact seen from the customer's side.

### Two honest caveats

- **`deliveries completed` went down, 229 → 207.** The dispatch board (`GET /delivery/deliveries`)
  took 91 of the rejections, and that board is the harness's offer-board workaround: every driver VU
  polls it on **one shared administrator token**, so they share one per-client bucket and behave like
  a single abusive client. That is a property of the harness, not of the platform — the same one
  `loadtest/README.md` calls the fulfilment ceiling — but it is a real cost of a per-client limiter
  meeting a design that has no per-driver "my offers" read model. A real driver app would not be
  affected; this run's drivers were.
- **s04 shed 0.99% while s05 shed 0.00%.** The concurrency limit binding on a burst at 12/s and not at
  16/s is not a contradiction — concurrency is rate × latency, and s04's ramp-in overlapped a slower
  moment. It is a reminder that the global limit is not a rate limit and will occasionally clip a
  spike well below the sustained knee.

### What it does not claim

It does not claim more capacity. Peak container CPU was 832% before and 867% after, against eight
cores — the machine was equally gone in both runs, exactly as the section above said it would be.
What changed is how a saturated system spends what it has: **the knee moved from a collapse at 32
customers/s to a plateau at 32 customers/s**, and the platform now degrades in a way a client can
respond to.

## Still open

- **Two Npgsql pools per service, from one connection string.** Every module host builds a
  `NpgsqlDataSource` (Dapper + the outbox/inbox jobs) *and* lets EF Core build its own from the same
  string, so a bound of 10 is really 20 per host. Passing the existing data source into
  `UseNpgsql(...)` would halve that and make the arithmetic obvious rather than explained. It is a
  change to `AddInfrastructure` and every module's registration, which is more than round one should
  carry.
- **The browse list is still uncached, and that is now a measured decision rather than an assumed
  one.** Revisit it on an environment where the generator is not co-located, or against a catalogue
  larger than 20 rows — both change the arithmetic that made change 3 not worth shipping. Doing so
  needs an answer for exact eviction first (a key-set registry, or a generation counter folded into
  the key); `ICacheService` removes one key at a time today.
- **The supply side generates more load than the demand side, and most of it is a workaround.**
  `driver.js` polls the *administrator's* delivery board and attempts claims, because the platform has
  no per-driver "my offers" read model (Milestone C, finding 3). What that costs, measured in the
  bounded-pool run: `RecordDriverLocationCommand` at 1,160 calls / 254 s of handler time and **923
  failed** `AcceptDeliveryOfferCommand` claims at 65 s — together outweighing every customer-facing
  handler combined, and buying 44 completed deliveries, about **21 wasted claims each**. It also caps
  measured fulfilment at ~0.9 deliveries/s no matter how many drivers run, because the waste grows
  with the pool. Until a "my offers" read model or the SignalR push exists, any capacity number for
  the fulfilment half of the platform is a number about the harness. **Round two added a second cost
  to it:** the whole driver pool polls that board on one shared administrator token, so a per-client
  limiter sees them as a single abusive client — 91 of the guardrail's 1,581 rejections landed there,
  and completed deliveries fell 229 → 207 because of it.
- **The limits are sized for one machine.** `GlobalConcurrencyLimit: 48` is Little's law over an
  8-core compose host with the generator co-located. On anything else it is a guess — including, and
  especially, a multi-replica deployment, where the *global* limit is per pod while the per-client
  windows are shared. Re-derive it from a ramp on the target environment;
  [`rate-limiting.md`](rate-limiting.md) §5 has the arithmetic to redo.
- **No Grafana panel for the shed rate yet.** The meter is registered and exporting; the panel needs
  the exported series names read off a live Prometheus first, because `ObservabilityAssetTests`
  rejects a dashboard that names a metric nothing emits. The k6 summary carries the number per phase
  in the meantime.
