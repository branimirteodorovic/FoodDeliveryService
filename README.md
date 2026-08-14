# FoodDeliveryService

A food-delivery platform built as **nine .NET 10 services behind a YARP API gateway** — Domain-Driven
Design, CQRS, an outbox/inbox event pipeline over MassTransit and RabbitMQ, Duende IdentityServer for
authentication, one PostgreSQL database per service, Redis for caching, distributed locking, driver
geolocation and the SignalR backplane, and OpenTelemetry wired end to end into Prometheus, Grafana,
Jaeger and Seq.

Everything runs locally with `docker-compose`; `Backend/deploy/` carries plain `kubectl` manifests for
a KinD cluster.

| | |
|---|---|
| Architecture and conventions | [`Backend/CLAUDE.md`](Backend/CLAUDE.md) |
| Observability — dashboards, alerts, correlation | [`Backend/docs/observability-backend.md`](Backend/docs/observability-backend.md) |
| Caching, keys and invalidation | [`Backend/docs/caching.md`](Backend/docs/caching.md) |
| Edge rate limiting | [`Backend/docs/rate-limiting.md`](Backend/docs/rate-limiting.md) |
| Load testing — method and full log | [`Backend/docs/load-testing.md`](Backend/docs/load-testing.md) |

```bash
cd Backend && docker-compose up -d
```

Gateway <http://localhost:3000> · Grafana <http://localhost:3100> · Jaeger <http://localhost:16686> ·
Seq <http://localhost:8081> · RabbitMQ <http://localhost:15672>

---

## Performance

Measured with a [k6 harness](Backend/loadtest/README.md) that authenticates as a real user, drives the
three journeys the platform exists for — browse → place order → track delivery — through the Gateway,
and has kitchen and driver traffic behind it closing the lifecycle. Every number below has a run
artifact behind it in [`Backend/loadtest/results/published/`](Backend/loadtest/results/published/).

> **The environment, because a capacity number without one is decoration:**
> compose · 8 vCPU · 7.6 GB to Docker · **1 replica per service** · **the load generator co-located on
> the same machine** · fixture of 20 restaurants × 24 menu items, 500 customers, 50 drivers.
> These numbers do not transfer to other hardware, and above roughly half the host's cores they
> describe the contest between the platform and its own generator.

### Steady state — 2 customer arrivals/s, five minutes

| | | |
|---|---|---|
| Requests | **24.4/s** | 8,211 over the run |
| Journey latency | **p50 8.5 ms · p95 31.3 ms · p99 77.3 ms** | against a 500 ms SLO |
| `POST /orders` | p95 **59.2 ms** | 121 orders placed, 0 failed |
| Errors | **0.00%** | 24,037 body-shape checks, all passing |

Run twice back to back; the two agree on journey p95 to **1.8%**, which is what makes anything below
worth quoting.

### Under load — ramping to 32 customer arrivals/s

| | Run-wide | Top step (32/s) |
|---|---|---|
| Requests served | 95,908 (**104/s**) | 17,060 in 90 s (**190/s**) |
| Journey latency | p50 116 ms · p95 476 ms · **p99 749 ms** | p50 229 ms · **p95 554 ms** |
| `POST /orders` | p95 **782 ms** · p99 1.25 s | |
| Errors | **0.20%** | 0.35% |
| Deliberately shed (`429`) | 1.66% | **4.99%** |
| Orders placed | **2,428** at 2.63/s, **0 failed** | |
| Lifecycle completed | 6,878 kitchen transitions · 207 deliveries delivered | |

### The saturation point, and what saturated

**The knee is between 26 and 32 customer arrivals/s on this environment, and the component that
saturates first is host CPU** — eight cores shared between the seven .NET services that were up,
Postgres, Redis, RabbitMQ, the tracing stack, *and the load generator itself*. The last time that was
broken down per container, the largest single consumer at the knee was password hashing: Identity took
2.3 of the 8 cores issuing tokens, against about 3 for the entire application path. That makes every
figure above a **lower bound** — every virtual user signs in, while a real population is mostly
returning users who already hold a token.

Past that knee, the platform used to collapse. It now sheds:

![Requests served and journey p95 at each ramp step, with and without the Gateway's rate limiter: without it the top step serves 1,968 requests at a p95 of 14.39 s; with it the same step serves 17,060 at 554 ms](Backend/docs/assets/loadtest/knee-cliff-vs-plateau.svg)

Same ramp, same machine, same afternoon, one variable — the Gateway's Redis-backed admission control.
At 32 arrivals/s the unguarded platform served **1,968** requests in the step, a seventh of what it had
served at 26/s, while offered load *rose* by a quarter; 32.4% of the requests in that step failed, and
across the run 4.02% of order placements did. With the limiter the same step served **17,060** at p95
**554 ms**, refusing **4.99%** of traffic to do it — and the shedding is ranked by route, so of 1,581
rejections **not one** was an order or delivery lifecycle transition, and not one was a health probe.

### Three measured fixes, one of them reverted

![Journey p95, journey p99 and POST /orders p95 across three controlled runs: before, after the event-pipeline change, and after bounding the connection pools](Backend/docs/assets/loadtest/round-one-fixes.svg)

The harness was built to find bottlenecks, and it did — each fix measured with a before/after of the
same profile on the same machine, one variable at a time:

- **The event pipeline had no index.** The outbox/inbox dispatch query sequentially scanned tables
  that only ever grow — 2,567 buffers and 16.05 ms per probe, twice per module per tick. A partial
  index took it to **1 buffer and 0.125 ms**, which is what made a 1-second tick affordable; with a
  larger batch the backlog drain went **2.96 → 9.44 rows/s** and order-placement failures 2.09% → 0%.
- **Every connection pool was unbounded.** Seven hosts × two pools × Npgsql's default of 100 against a
  Postgres running `max_connections=100` produced **678 × "sorry, too many clients already"** in a
  three-minute run, with 80 of 88 backends *idle*. Bounding the pools took journey **p99 from 2.31 s
  to 1.15 s** and `POST /orders` p95 from 1.43 s to 919 ms.
- **The cache that looked obvious was not.** The one uncached browse query measured 242 ms against 26
  ms for its cached neighbours — until the connection fix landed, after which the same query measured
  **26.3 ms**. The 9× gap was 12%: it had never been a missing cache, it was the query holding a
  connection while the pool starved. The cache was written, measured, and **reverted**.

That third one is the most useful entry in the log, and the reason to read it: the first bottleneck in
a queue makes every component behind it look guilty.

### About "100,000 concurrent users"

A single-replica stack on eight shared cores will not serve 100,000 concurrent users, and this README
is not going to claim it does. What was measured, and what follows from it:

> ~**190 journey requests/second** and ~**2.6 orders/second** per single-replica stack at p95 554 ms,
> shedding 5%. 100,000 concurrent users acting once every 30 seconds is on the order of **3,300
> requests/second** — roughly **17–20 stacks of that size**, *if* the single Postgres, Redis and
> RabbitMQ behind them scale too, which this feature did not measure because host CPU saturated first.

Running more than one replica of anything is blocked on three specific hazards, one of which this work
fixed (`FOR UPDATE` without `SKIP LOCKED` on all eleven dispatch queries) and two of which are open and
named in [`KUBERNETES_PHASE2_PLAN.md`](Backend/KUBERNETES_PHASE2_PLAN.md) §5.1.

**The full method, every threshold, the complete bottleneck log including the fixes that did not work,
and how to reproduce any number on this page:**
[`Backend/docs/load-testing.md`](Backend/docs/load-testing.md).
