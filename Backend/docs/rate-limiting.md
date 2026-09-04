# Capacity guardrails at the edge

> Delivered by **Feature 3.5 — Load Testing & Scalability Demonstration**
> (`LOADTESTING_PHASE3_PLAN.md`), **Milestone G**. It closes a gap that was pre-existing and already
> written down: `KUBERNETES_PHASE2_PLAN.md` §7 recorded that *the Gateway has no rate limiter — a
> Feature 1.3 task that was never built*, and Milestone D's ramp made the consequence concrete.
> The measurements this is sized from are in [`load-testing.md`](load-testing.md).

## 1. The problem it solves, stated as behaviour

A platform with no admission control does not degrade. It queues.

Past the knee, every arriving request is accepted, every one of them waits behind the ones already
in flight, latency climbs until clients time out, and the platform ends up having done the work for
requests nobody is still waiting for. The measured shape of that (`load-testing.md`): at 26
customers/s the reference environment recorded journey p95 **1.85 s** and a 3.8% error rate, where
the errors are timeouts — a client-side symptom of a server-side queue.

The shape instead, and this is measured rather than intended — the same eight-step ramp either side
of one variable, at its top step (32 customers/s, well past the knee):

| At 32 customers/s | Without a limiter | With one |
|---|---|---|
| Requests served in the step | 1,968 | **17,060** |
| journey p95 | 14.39 s | **554 ms** |
| Errors | 32.38% | **0.35%** |
| Refused | 0 | 4.99%, explicitly |
| What the client learns | a timeout — indistinguishable from the platform being down | `429` + `Retry-After` — back off by this much |

The full before/after, the per-route breakdown of what was shed, and the two honest caveats are in
[`load-testing.md`](load-testing.md) → *Round two: admission control*. The one-line version: shedding
one request in twenty let the other nineteen be served in half a second.

**Shedding is not a latency optimisation and it does not raise capacity.** Peak container CPU was
832% before and 867% after, on eight cores — the machine was equally saturated in both runs. It
decides *how* a fixed capacity is spent when more is asked of it than it has.

## 2. Two limits, doing two different jobs

Both are configured in the `RateLimiting` section (§5) and both are enforced by ASP.NET Core's
built-in rate limiter, chained, in front of YARP.

### The global concurrency limit — the one that makes the plateau

`GlobalConcurrencyLimit` bounds how many requests may be in flight through the Gateway at once,
across every client. **This is what turns a cliff into a plateau**, and the per-client windows are
not: past the knee the problem is not one client misbehaving, it is every client arriving at once,
and a per-client budget none of them individually exceed does nothing about that.

The default of **48** is Little's law over the round-one measurements: at the knee (20 customers/s)
the reference run sustained ~50 requests/s at ~213 ms average, so ~11 requests were in flight.
48 leaves roughly 4× headroom — ordinary bursts pass untouched, and sustained overload, where
latency climbs and concurrency climbs with it, is what gets shed.

`GlobalQueueLimit` is **0** on purpose. Queuing at the edge converts a fast rejection into a slow
one: the client waits, times out anyway, and the platform spent the connection for nothing.

### The per-client fixed window — the one that stops one client being everyone's problem

One counter per `{tier}:{client}` per `WindowSeconds`. Fixed window rather than sliding or token
bucket, because it is one `INCR` plus a conditional `PEXPIRE` — a single round trip on the hot path
of every request through the single public entry point. A sliding window costs a sorted set per
client and a read-modify-write, and the burst tolerance it buys is not worth that at the edge.

**The partition is the subject claim when authenticated, the IP otherwise**
(`RateLimitClient.Resolve`). Both directions matter:

- An IP is not an identity. A whole office, a mobile carrier's NAT or a corporate VPN shares one, so
  partitioning authenticated traffic by IP throttles a hundred innocent users because of one.
- An account is not an IP. Counting a signed-in customer by address lets the same account walk
  around its budget by changing networks.
- An anonymous request has nothing else to be counted against — `users/register` is unauthenticated
  by design and is exactly the endpoint an abusive client reaches for.

**Behind a proxy, the IP half depends on forwarded headers.** `RemoteIpAddress` is the *proxy's*
address once anything terminates TLS in front of the Gateway, so every anonymous caller on the
platform would share one partition and the per-client window would quietly become a second global
limit. Feature 3.7 Milestone D added `app.UseEdgeForwardedHeaders()` ahead of this middleware to fix
that — but it trusts **nothing** until a deployment names a proxy address or network, because an
unrestricted `X-Forwarded-For` would let a client pick its own partition key, which is worse than the
bug it repairs. A deployment behind an ingress must set `ForwardedHeaders:KnownNetworks`; the Gateway
logs a warning at startup while nothing is trusted. `docs/security.md` §5.2.

Consequence for the pipeline: `app.UseEdgeRateLimiting()` sits **after** `UseAuthentication()`.
Before it, `HttpContext.User` is empty and every request would be an IP partition. The cost is that
a flood pays for JWT validation before being shed — signature verification against cached signing
keys, no I/O, and orders of magnitude cheaper than the proxied round trip it prevents.

## 3. Shaped shedding: the route ranking

A limiter that sheds every route equally protects the platform and ruins it at the same time. A
`429` on `GET restaurants` is a slightly worse browse; a `429` on
`POST delivery/deliveries/{id}/delivered` strands a delivery that has already happened in the real
world — the food is at the door and the platform is refusing to record it.

So routes are ranked, in `RateLimitRoutePolicy` — that table **is** the policy, which is why it is a
table and not a chain of conditionals in the middleware.

| Tier | Routes | Per-client budget | Global concurrency limit | Rationale |
|---|---|---|---|---|
| **Exempt** | `/health`, `/health/live`, `/health/ready`, `hubs/**` | none | not applied | The blackbox exporter probes every host every 15 s from outside and a throttled probe is a *false outage alarm*. SignalR's negotiate-then-connect is one logical connection over two requests, and a long-lived WebSocket in a concurrency slot would exhaust the global limit with clients that are idle by design. |
| **Critical** | `POST orders/{id}/{accept,reject,preparing,ready,cancel}`, `POST delivery/deliveries/{id}/{accept,reject,picked-up,delivered}` | 300 / window | **bypassed** | Advancing work the platform already accepted. Shed last: when the Gateway runs out of capacity, browsing is refused and a driver completing a delivery still gets through. |
| **Write** | `POST orders`, driver location and availability, registration, catalogue administration, and every unrecognised mutation | 60 / window | applied | Creates new work. A rejection costs a retry and strands nothing — the order was never placed. |
| **Read** | every `GET`/`HEAD`/`OPTIONS` | 200 / window | applied | 70% of the load model by design, and the cheapest thing to lose. |

Two properties of the table worth knowing before changing it:

- **The critical rules are `POST`-only.** The *transition* is what must not be shed; a `GET` on the
  same delivery is a read like any other.
- **An unrecognised mutation lands in `Write`, not outside the limiter.** A new lifecycle transition
  that belongs in `Critical` earns a line in the table; until it does, it is merely limited more
  tightly than intended — which is the direction to fail in.

The tier is part of the counter key, not just the budget: a client that has spent its read budget
browsing must still be able to complete a delivery, which it cannot do if both share a counter.

## 4. Why the counters are in Redis

**Per-pod in-memory buckets multiply the effective limit by the replica count, and never say so.**
`KUBERNETES_PHASE2_PLAN.md` §5.4 named this before the limiter existed: the Gateway can scale freely
*because* it has no state — a limiter is the first thing that gives it some. Four pods with a
200/window limit is really an 800/window limit, and nobody finds out until the day it matters.

So `IRateLimitStore` is Redis-backed (`Gateway/RateLimiting/RedisRateLimitStore`), on the same single
logical Redis the cache, the distributed lock, the driver GEO set and the SignalR backplane already
share ([`caching.md`](caching.md) §1). The whole decision is one Lua script, so `INCR`, the
first-write `PEXPIRE` and the `PTTL` read cannot interleave with another pod's — without it, two
requests racing on a new key can both see `current == 1`, or a key can be incremented by a pod that
dies before setting an expiry, leaving a counter that never resets and a client throttled forever
(the script repairs that case rather than living with it).

### Where the Gateway's Redis connection comes from

The Gateway takes **no `Common.Infrastructure` dependency** — it is a proxy, not a module host — so
it cannot call `RedisConnectionOptions.Create`. It opens its own connection with the same two
hardening defaults, deliberately duplicated and commented as such in
`Gateway/RateLimiting/GatewayRateLimitingExtensions`: `AbortOnConnectFail = false` and an exponential
reconnect back-off. **If those defaults change in `Common.Infrastructure/Caching`, this is the other
place to change.**

### It fails open, and Redis is not a readiness dependency

When the store is unreachable, `TryAcquireAsync` admits the request and logs a warning at most once
every 30 seconds. A limiter that failed closed would turn a cache blip into a total outage of the
only way into the platform — the guardrail becomes the incident. The global concurrency limit is
in-process and keeps working throughout, so the ceiling that actually prevents collapse is still
enforced while the per-client budgets are not.

For the same reason Redis is **not** added to the Gateway's readiness check. The Gateway's readiness
deliberately equals its liveness (one dead downstream cluster must not take the single public entry
point out of rotation); a Redis outage degrades the guardrail, not the gateway.

### The in-memory fallback is Development-only

`InMemoryRateLimitStore` is correct for one process and wrong for two. It is selected only when
`ConnectionStrings:Cache` is absent **and** the host is in Development, it logs a warning at startup
saying the limits are per process, and outside Development the host refuses to start rather than
enforce a limit it cannot honour. Same shape as the cache's and the lock's fallbacks
([`caching.md`](caching.md) §4).

## 5. Configuration

`RateLimiting`, in the Gateway's `appsettings.json`. Every value is tunable because every value is
environment-specific — the defaults come from an 8-core compose host, and a machine with four times
the cores wants more concurrency.

| Key | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Kill switch. Off removes the middleware entirely — not a generously configured limiter, which would be a latent `429`. |
| `GlobalConcurrencyLimit` | `48` | Requests in flight, all clients, excluding exempt and critical routes. |
| `GlobalQueueLimit` | `0` | Queue in front of that limit. Zero on purpose (§2). |
| `WindowSeconds` | `10` | Fixed-window length for the per-client budgets. |
| `ReadPermitLimit` | `200` | 20 req/s sustained per client. A real browse with think time is ~0.3/s. |
| `WritePermitLimit` | `60` | 6 req/s sustained per client. |
| `CriticalPermitLimit` | `300` | A backstop against a looping client, not a capacity control. |
| `DefaultRetryAfterSeconds` | `1` | `Retry-After` when the rejection carries no window — a concurrency rejection, where the slot frees in milliseconds. |
| `KeyPrefix` | `ratelimit` | Namespaces the counters in the shared Redis. |

`ConnectionStrings:Cache` is the store. In compose it is `fooddeliveryservice.redis:6379`; in
`deploy/k8s` the Gateway now reads the same `ConnectionStrings__Cache` key from `platform-secrets`
that the module hosts do.

## 6. What a rejected request looks like

```http
HTTP/1.1 429 Too Many Requests
Retry-After: 6
Content-Type: application/problem+json

{"type":"https://tools.ietf.org/html/rfc6585#section-4","title":"Too Many Requests","status":429,
 "detail":"The gateway is shedding load to stay available. Retry after the interval in the Retry-After header."}
```

The body is pre-rendered rather than serialized per request: the rejection path has to be cheaper
than the request it is refusing, or shedding becomes a load of its own at exactly the moment there
is none to spare. The correlation id is not in the body because `UseRequestCorrelation()` already
echoes it on the response header, so a shed request is still findable in Seq.

`Retry-After` is the remaining window for a per-client rejection and `DefaultRetryAfterSeconds` for a
concurrency one. Without it a client can only retry immediately, which is precisely the behaviour
that turns an overloaded platform into an unavailable one.

## 7. Observability

The Gateway registers ASP.NET Core's own meter, `Microsoft.AspNetCore.RateLimiting`, alongside the
YARP one. Nothing emits it until the middleware is in the pipeline, which is the point — the limiter
and its metrics arrive together.

| Instrument | What it answers |
|---|---|
| `aspnetcore.rate_limiting.requests` (tagged by result) | the accepted/rejected split — the rejected fraction, explicitly |
| `aspnetcore.rate_limiting.active_request_leases` | how close the global concurrency limit is to binding |
| `aspnetcore.rate_limiting.request_lease.duration` | how long admitted requests hold a permit |

Startup logs the whole configuration on one line, including whether the counters are shared or
per-process, so "what were the limits on that box" is answerable from the log rather than from the
image.

On the harness side, `loadtest/` records `requests_throttled` for every request
(`loadtest/lib/http.js`), thresholded per profile and per phase — see
[`../loadtest/README.md`](../loadtest/README.md) → *The capacity guardrail*.

## 8. Tests

| Test | What it pins |
|---|---|
| `Common.UnitTests/RateLimiting/RateLimitRoutePolicyTests` | the route ranking, including that lookalike paths (`/healthz`, `/hubsy/…`, a bare `/hubs`) do **not** inherit the exemption and that a trailing slash cannot walk around it |
| `Common.UnitTests/RateLimiting/RateLimitClientTests` | partition selection — authenticated → subject (mapped or not), anonymous → IP, unauthenticated claims ignored, two subjects behind one NAT kept apart |
| `Common.UnitTests/RateLimiting/InMemoryRateLimitStoreTests` | the fixed-window contract the Lua script is written to match, on a hand-cranked clock |
| `Common.UnitTests/RateLimiting/EdgeRateLimitingTests` | the middleware over real HTTP: N served and the N+1th `429` with `Retry-After`, exempt paths never limited, the kill switch actually removing the middleware, and — the interesting one — **a shed browse and an admitted `ready` while the single concurrency slot is held**, which is the shaped-shedding claim made concrete |

They live in `Common.UnitTests` because the code does: the limiter is in `Common.Presentation` for
the same reason the health probes and `AddHostTelemetry` are — the Gateway needs it without becoming
a module host. Only the Redis store lives in the Gateway.

### And once against the real thing

The tests above run on the in-memory store, so the Redis path was also driven by hand — the real
Gateway, the real Lua script, the compose Redis, `ReadPermitLimit` lowered to 5 so it is legible:

```
GET  /restaurants   ×8    401 401 401 401 401  429 429 429     Retry-After: 30
GET  /health/live   ×20   200 ×20                              exempt
POST /orders/{id}/ready ×8  401 ×8                             separate counter, unaffected
redis> --scan --pattern 'ratelimit:*'
ratelimit:read:ip:::1
ratelimit:critical:ip:::1
```

The `401`s are the point rather than a flaw: the limiter sits before `UseAuthorization`, so an
admitted request goes on to be rejected by the authorization policy and a shed one never gets that
far. Five admitted, the sixth shed with the remaining window on the header, probes untouched, and
the critical tier still holding its own budget after the read tier was spent.

## 9. Deliberately not built

- **A per-endpoint or per-tenant policy surface.** Four tiers over a path table is enough for a
  platform with four modules; `[EnableRateLimiting("name")]` per endpoint would put the policy in
  seven Presentation projects instead of one table.
- **A sliding window or token bucket.** §2 — the burst tolerance is not worth the extra round trip
  on the edge's hot path.
- **Limiting inside the module hosts.** Hard Rule 10 means all external traffic passes the Gateway,
  so one limiter is sufficient; a second on every hop multiplies its own limit and sheds traffic that
  has already been admitted.
- **Cost-based limiting** (a heavy query counting for more than a cheap one). It needs a per-endpoint
  cost model that nothing currently measures. The tiers are a coarse version of the same idea.
- **A Grafana panel for the shed rate — not yet, and deliberately not guessed.** The meter is
  registered and exporting, but the collector renames instruments on the way to Prometheus and
  `ObservabilityAssetTests` fails the build if a dashboard names a metric nothing emits
  ([`observability-backend.md`](observability-backend.md)). The panel is worth adding once the
  exported series names have been read off a live Prometheus rather than inferred from the
  instrument names. Until then the shed fraction is in the k6 summary, per phase, which is where the
  Milestone G before/after is read from anyway.
