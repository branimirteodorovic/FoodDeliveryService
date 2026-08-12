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

## Still open after round one

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
  the fulfilment half of the platform is a number about the harness.
