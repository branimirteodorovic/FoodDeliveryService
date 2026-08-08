# Caching, locking and the Redis connection

> Delivered by **Feature 2.3 — Caching with Redis** (`CACHING_PHASE2_PLAN.md`). Milestones A–E built
> the cache-aside helper, the declarative query caching, the invalidation model, the distributed lock
> and the hit/miss counters; **Milestone F** (this document) covers the connection itself — running
> the same code against a local container or Azure Cache for Redis, and what the health check says
> when it is gone.

## 1. Redis is not just a cache here

Four unrelated capabilities sit on the one Redis instance per environment. This matters for sizing
and failover: an outage is not "a slower service", it takes live driver assignment down.

| Concern | Owner | Keys / channels | Losing Redis means |
|---|---|---|---|
| Cache-aside reads | Restaurants (`QueryCachingBehavior`) | `restaurants:menu\|detail\|item:{id}` | Every read falls through to Postgres — degraded, still correct |
| Permission cache | every service (`PermissionService`) | `user_permissions:{identityId}` | One MassTransit RPC to Users per request — degraded, still correct |
| Distributed lock | Delivery (`IDistributedLock`) | `delivery:offer-lock:{deliveryId}`, `delivery:driver-lock:{driverId}` | Driver assignment **fails** (`AssignmentInProgress`) rather than double-booking |
| Live driver positions | Delivery (`RedisDriverLocationStore`) | GEO set `delivery:drivers:available`, per-driver hash, pub/sub channel `delivery:driver-locations` | "Who is nearest" has **no other store** — assignment stops |
| SignalR backplane + routing map | RealTime | SignalR-internal, `rt:order:{id}`, `rt:driver:{id}`, `rt:order-driver:{id}` | Fan-out no longer crosses instances |

The last two are why Milestone F is not a cache-hit-rate concern: pick a tier with replication and
zone redundancy, not the cheapest one that fits the working set.

## 2. What is cached, for how long, and what evicts it

| Key | Written by | TTL | Invalidated by |
|---|---|---|---|
| `restaurants:menu:{restaurantId}` | `GetMenuQuery` | 5 min (`RestaurantCacheKeys.Expiration`) | `CreateMenuItem`, `UpdateMenuItem`, `SetMenuItemAvailability`, `CreateMenuCategory`, `UpdateMenuCategory` |
| `restaurants:detail:{restaurantId}` | `GetRestaurantQuery` | 5 min | `UpdateRestaurant` |
| `restaurants:item:{menuItemId}` | `GetMenuItemQuery` | 5 min | `UpdateMenuItem`, `SetMenuItemAvailability` |
| `user_permissions:{identityId}` | `PermissionService` | 5 min | nothing — TTL only, so a permission change takes up to 5 minutes to apply |

Keys are never concatenated at a call site: `CacheKeys.Create(area, entity, id)` builds them, and each
module keeps its own convention class (`RestaurantCacheKeys`, `DeliveryLocks`) so the read side and
the write side cannot drift onto different strings. Anything not listed above is **not cached** —
notably `GetRestaurants` (paged + filtered; too many key permutations for the hit rate).

Defaults for anything that does not pass an explicit TTL live in the `Caching` configuration section
(`CachingSettings`): `DefaultExpiration` 2 min, `JitterPercentage` 0.10. The jitter is ±10% per entry
so a batch of keys written together does not expire on the same tick and stampede Postgres.

### How a read becomes cached

A query opts in by implementing `ICachedQuery<TResponse>` (key + expiration); `QueryCachingBehavior`
sits in the MediatR pipeline after validation and wraps the handler in
`ICacheService.GetOrCreateAsync`. Handlers stay pure Dapper — there is no caching code inside them.
Only successful results are stored: a `Result.Failure` is never cached, so a transient NotFound
cannot pin itself for a TTL.

### The invalidation model: inline, not outbox

Eviction is a `cacheService.RemoveAsync(...)` in the **command handler, immediately after
`SaveChangesAsync`** — not a domain-event handler dispatched by `ProcessOutboxJob`. Two reasons,
both learned during Milestones B and C:

- **Freshness.** Outbox dispatch lag would make the guarantee "fresh within one job tick" instead of
  "the next read is fresh".
- **Correctness.** Several restaurant domain-event handlers read `restaurants:detail`/`restaurants:item`
  back through `ISender` to build their integration-event snapshot. With eviction lagging behind that
  read, a snapshot published stale data — a real bug that broke two tests before it was fixed.

The TTL remains the safety net for the crash-between-save-and-evict window. Cross-instance pub/sub
eviction is deliberately out of scope: keys are owned by exactly one service and every writer evicts
synchronously.

## 3. Observability

`CacheService.GetAsync` — the single choke point every read passes through — records `cache.hits` /
`cache.misses` on the `FoodDeliveryService.Cache` meter, tagged `cache.key_prefix` (the key minus its
id segment, e.g. `restaurants:menu`), plus a debug-level log line carrying the full key.

**These are collected from Telemetry 2.4 Milestone A (2026-07-31).** `AddInfrastructure` now stands up
a metrics reader alongside the tracer and registers `CacheDiagnostics.MeterName` via `AddMeter`, so the
counters leave the process over OTLP. A backend to point them at — Collector → Prometheus → Grafana,
including the hit-rate panel — is Telemetry Milestone E. Redis commands themselves are already traced
(`AddRedisInstrumentation`), so a cache miss and the Postgres read behind it show up in Jaeger as one
trace.

## 4. The connection

Every host reads one connection string, `ConnectionStrings:Cache`, and passes it to
`AddInfrastructure`. `RedisConnectionOptions.Create` parses it and applies exactly two hardening
defaults before anything connects:

- **`AbortOnConnectFail = false`** (forced, even if the connection string sets it true). A cache that
  is briefly unreachable must not take the host down, and Azure Cache for Redis *requires* it — nodes
  are patched and failed over underneath you. `ConnectionMultiplexer.Connect` therefore returns a
  usable, self-reconnecting multiplexer even against a server that is down; it throws only when the
  connection string itself is unusable, which fails startup on purpose.
- **Exponential reconnect back-off** (1 s → 30 s) instead of StackExchange.Redis' linear default, so
  a fleet of replicas that lost the same node does not retry in lockstep.

Everything else — endpoint, credential, TLS, timeouts, retry counts — comes from the connection
string, so it stays the only thing that differs between environments. TLS needs no special handling:
StackExchange.Redis recognises the Azure Cache DNS suffixes and enables it itself (pinned by
`RedisConnectionOptionsTests`).

The resulting `IConnectionMultiplexer` is registered once and shared by the distributed cache, the
distributed lock, Delivery's GEO store, RealTime's location subscriber and the Redis health check.
The SignalR backplane must own its own connection, so it is handed the same
`RedisConnectionOptions.Create(...)` result rather than the raw string — otherwise it would be the
one connection ignoring the reconnect policy.

### When Redis is unreachable at startup

`AddInfrastructure` takes a `allowInMemoryCacheFallback` flag; every host passes
`builder.Environment.IsDevelopment()`.

| | Development (`true`) | Anywhere else (`false`) |
|---|---|---|
| Redis reachable | Redis cache + Redis lock | Redis cache + Redis lock |
| Redis unreachable | In-process cache + **in-process lock**, one `WARN` line at boot (`InMemoryFallbackWarning`) | Keeps the reconnecting Redis connection; the Redis health check reports unhealthy until it is back |

The fallback is a local-convenience feature only. `InMemoryDistributedLock` excludes callers *inside
one process*, which stops protecting anything the moment a second replica exists — a silent
degradation is worse than an unready pod, so outside development the host stays on Redis and lets the
probe do its job.

### Health check

Every host that uses Redis registers `.AddRedis(sp => sp.GetRequiredService<IConnectionMultiplexer>())`
— it probes the very multiplexer the application uses, TLS and reconnect policy included, instead of
opening a second connection with different settings. It is reported by `GET /health` on each host
(Notifications :5100, Orders :5200, Restaurants :5300, Users :5400, Delivery :5500, RealTime :5600);
the Gateway and Identity use no cache and register no Redis check.

> When Feature 2.4 Milestone C lands the `/health/live` + `/health/ready` split
> ([contract](health-probe-contract.md)), this check is a **`ready`** one: a Redis outage must pull a
> pod out of rotation, never restart it.

## 5. Running against Azure Cache for Redis

No code change — only `ConnectionStrings:Cache`.

1. Provision the cache. Use a tier with replication (Standard or higher; Premium/Managed for zone
   redundancy) — §1 explains why this is not an optional nicety here. Keep **TLS-only** and the
   minimum TLS version at 1.2, and leave the non-TLS port disabled.
2. Copy the primary connection string from *Access keys*. It looks like:

   ```
   {name}.redis.cache.windows.net:6380,password=...,ssl=True,abortConnect=False
   ```

3. Supply it as `ConnectionStrings__Cache` per host — an app setting / Key Vault reference in App
   Service or Container Apps, a `Secret` mounted as an environment variable under AKS. Never commit it
   to `appsettings.json`; the committed files carry an empty `Cache` string precisely so the
   environment must provide one.
4. Deploy with `ASPNETCORE_ENVIRONMENT` set to anything other than `Development`, so the in-process
   fallback is off (§4).
5. Firewall/network: the cache must be reachable from the service subnet — public endpoint plus
   firewall rules, or a private endpoint. `ConnectTimeout` can be raised in the connection string
   (`,connectTimeout=15000`) if the first connection crosses a cold private link.

### Manual smoke check

Not automated — the integration suites run against the Testcontainers Redis. Point one service (start
with Restaurants: it owns the cached reads) at the Azure endpoint and walk through:

| # | Do | Expect |
|---|---|---|
| 1 | `GET /health` on the host | `200`, `redis` entry `Healthy` |
| 2 | `GET restaurants/{id}/menu` twice | Second response measurably faster; Jaeger shows the Redis `GET` and no Postgres span on the second |
| 3 | In Azure portal → *Console*: `KEYS restaurants:*` | `restaurants:menu:{id}` present with `TTL` ≈ 300 s |
| 4 | `PUT restaurants/{restaurantId}/menu-items/{menuItemId}` (reprice), then re-read the menu | New price immediately; the key was evicted and rewritten |
| 5 | Authenticate as any user twice | `user_permissions:{identityId}` present after the first call |
| 6 | Stop the client, wait past the TTL, read again | Key gone, repopulated on the next read |
| 7 | Restart/failover the cache while the service runs | Requests recover without restarting the service (this is what `abortConnect=false` + exponential retry buys); `WARN`/`ERROR` bursts in Seq, then quiet |
| 8 | Two concurrent driver-offer triggers (Delivery) | Exactly one offer — the lock works over TLS the same as locally |

Step 7 is the one worth doing deliberately: it is the only way to see the reconnect policy work, and
it is the failure mode a managed cache actually has.
