# Feature 2.3 — Caching with Redis — Implementation Plan

> Sixth implementation plan, after `RESTAURANTS_PHASE1_PLAN.md`, `ORDERS_PHASE1_PLAN.md`, `NOTIFICATIONS_PHASE1_PLAN.md`, `DELIVERY_PHASE2_PLAN.md` and `REALTIME_PHASE2_PLAN.md`. This one covers **Feature 2.3 — Caching with Redis**, the third feature of **Phase 2** in `FoodDelivery_ProjectPlan.md`.

> **Scope for this iteration:** turn the *existing but barely-used* Redis cache layer into a first-class, reusable capability, and apply it where it earns its keep. Concretely: (1) promote the ad-hoc get/miss/set already living inside `PermissionService` into a **shared cache-aside helper + a CQRS query-caching pipeline behavior**, (2) **cache Restaurant menu/detail reads** — the highest read:write ratio in the system — behind that behavior, (3) **invalidate those entries** off the domain events restaurants already raise on every menu change, and (4) add a **Redis distributed lock** around Delivery's driver-assignment routine, which today has a genuine double-offer / same-driver-to-two-orders race. Each milestone is independently shippable, unit- **and** integration-tested against a real Redis Testcontainer, and sized for a single PR review.

> **Sequencing:** execute this plan **after `REALTIME_PHASE2_PLAN.md` (Feature 2.2) ships.** There is no code dependency in either direction — the two plans use Redis for disjoint purposes and touch disjoint files — but the order is intentional; §10 records the reconciliation so nothing is built twice.

Decisions locked in for this plan:

- **Build on what exists — do not add a second cache abstraction.** `ICacheService` (`Common.Application/Caching`) + `CacheService` (`Common.Infrastructure/Caching`, over `IDistributedCache`/StackExchange.Redis) already ship and are wired globally in `InfrastructureConfiguration`. Today they cache exactly one thing: user permissions for 5 min (`PermissionService`). This feature **extends** that surface (`GetOrCreateAsync`, a caching pipeline behavior, invalidation, a distributed lock) rather than introducing a parallel one. Redis is already in `docker-compose` and available as a Testcontainer, so every cache path is testable offline.
- **Reads are cached declaratively via a pipeline behavior, not by hand in each handler.** The codebase already composes MediatR behaviors (ExceptionHandling → RequestLogging → Validation). We add a `QueryCachingBehavior` gated by an `ICachedQuery` marker, so a query opts in by declaring a cache key + TTL and the handler stays pure Dapper. This keeps caching *out* of the query handlers (which the application-layer rules want kept to a single Dapper read) and makes "is this cached?" answerable from the query type alone.
- **Menus are the target; the menu is cached as one composed object per restaurant.** A restaurant menu is read on every browse/checkout and changes rarely — the canonical cache-aside example. `GetMenuQuery` already returns the whole menu (`MenuResponse`) in one `QueryMultiple` round-trip, so one Redis key (`restaurants:menu:{restaurantId}`) caches the whole thing and one `RemoveAsync` invalidates it. `GetRestaurant` and `GetMenuItem` get the same treatment; **list/search (`GetRestaurants`) is deliberately *not* cached** in this iteration (paged, filtered, many key permutations, invalidation fan-out not worth it — noted under §Out of scope).
- **Invalidation rides the domain events restaurants already raise — except where Milestone B found it couldn't.** Every menu mutation already raises a domain event (`MenuItemPriceChangedDomainEvent`, `MenuItemAvailabilityChangedDomainEvent`, `MenuItemAdded/Updated`, `MenuCategoryAdded/Updated`, `RestaurantDetailsUpdated`, `RestaurantAddressUpdated`), and the original intent was thin outbox-driven `DomainEventHandler`s calling `ICacheService.RemoveAsync(...)`. **Correction from Milestone B:** `restaurants:detail:{id}` and `restaurants:item:{id}` turned out not to tolerate that outbox lag — `RestaurantRegisteredDomainEventHandler`/`RestaurantAddressUpdatedDomainEventHandler`/`MenuItemAdded|Updated|PriceChangedDomainEventHandler` read those same cached queries via `ISender` to build their integration-event snapshot, so a lagging eviction let a snapshot publish stale data (broke 2 real tests; see §4). Those two keys are now evicted **inline in the command handler** (`UpdateRestaurantCommandHandler`, `UpdateMenuItemCommandHandler`, `SetMenuItemAvailabilityCommandHandler`), synchronously, right after `SaveChangesAsync`. **Milestone C then extended the same inline approach to `restaurants:menu:{id}`** across the five menu-mutating command handlers rather than adding outbox-driven `DomainEventHandler`s: `menu` has no internal snapshot reader so the domain-event route would have been *correct*, but it would have traded next-read freshness for outbox dispatch lag and left the feature with two invalidation mechanisms. Net result: **one** invalidation strategy — synchronous, post-`SaveChangesAsync`, keys built through `RestaurantCacheKeys` — with TTL as the safety net. See §4.
- **Distributed locking is scoped to the one place that actually races: driver assignment.** `DeliveryAssignmentService.OfferNextAsync` reads a `Pending` delivery, finds the nearest available driver, and offers it. Two concurrent triggers — a rejection re-offer racing the `ProcessExpiredOffersJob` tick, or two different deliveries both selecting the same nearest driver at once — can double-offer or hand one driver two orders. A short-lived Redis lock keyed per candidate driver (and per delivery) closes that window. This is the project plan's "distributed locking … so two simultaneous requests don't both assign the same driver." We use a small `IDistributedLock` abstraction (SET NX PX + token-checked release, i.e. single-node Redlock) — no new heavyweight dependency unless we choose RedLock.net in review.
- Reference implementations to mirror: **`PermissionService`** for the existing cache-aside shape, the **`Common.Application/Behaviors`** pipeline for where the caching behavior slots in, **Restaurants** domain events for invalidation triggers, and **`DeliveryAssignmentService` + `ProcessExpiredOffersJob`** for the lock site. Test scaffolding: the **Restaurants** and **Delivery** integration suites (`IntegrationTestWebAppFactory` + Testcontainers + real Duende JWTs) already spin up Redis.

---

## 0. What already exists today (no work required)

| Capability | Where | Status |
|---|---|---|
| `ICacheService` (`GetAsync`/`SetAsync`/`RemoveAsync`, JSON over `IDistributedCache`) | `Common.Application/Caching` + `Common.Infrastructure/Caching/CacheService.cs` | **shipped** |
| Redis wiring (`ConnectionMultiplexer`, `AddStackExchangeRedisCache`, in-memory fallback) | `Common.Infrastructure/InfrastructureConfiguration.cs` | **shipped** |
| Redis in local compose + as Testcontainer in every integration suite | `docker-compose.yml`, `IntegrationTestWebAppFactory` | **shipped** |
| Permission caching (5 min) — the *only* current cache consumer | `{Module}.Infrastructure/Authorization/PermissionService.cs` | **shipped** |
| Menu/detail read queries (pure Dapper, **uncached**) | `Restaurants.Application/.../GetMenu`, `GetRestaurant`, `GetMenuItem` | **shipped, to be cached** |
| Restaurant mutation domain events (invalidation triggers) | `Restaurants.Domain/Restaurants/*DomainEvent.cs` | **shipped** |
| Driver assignment routine (the lock site) | `Delivery.Infrastructure/Assignment/DeliveryAssignmentService.cs` | **shipped, to be locked** |

**Net:** the plumbing is in; this feature is about *leverage and correctness*, not new infrastructure. That keeps every milestone small.

---

## 1. Milestone overview

| # | Milestone | Layer touched | New surface | PR size |
|---|---|---|---|---|
| **A** | Shared cache-aside library | `Common.Application` + `Common.Infrastructure` | `GetOrCreateAsync`, `CacheKeys`, TTL+jitter options; refactor `PermissionService` onto it | S |
| **B** | Declarative query caching + menu reads | `Common.Application` + `Restaurants.Application` | `ICachedQuery`, `QueryCachingBehavior`; opt in `GetMenu`/`GetRestaurant`/`GetMenuItem` | M |
| **C** | Menu cache invalidation ✅ | `Restaurants.Application` | inline eviction of `restaurants:menu:{id}` in the five menu-mutating command handlers — `detail`/`item` keys already inline-evicted in B | S |
| **D** | Distributed lock for driver assignment ✅ | `Common.*` + `Delivery.Infrastructure` | `IDistributedLock` + Redis impl; wrap `OfferNextAsync`/accept | M |
| **E** | Cache observability ✅ | `Common.Infrastructure` | hit/miss OTel counters + structured logs in behavior/CacheService | S |
| **F** | Azure Cache for Redis + hardening ✅ | `Common.Infrastructure` + hosts | hardened `ConfigurationOptions`, environment-gated in-memory fallback, health check on the shared multiplexer, `docs/caching.md` | S |

Dependency order: **A → B → C**, **A → D**. E and F are independent add-ons. B and D can be developed in parallel after A.

---

## 2. Milestone A — Shared cache-aside library (Common)

**Goal:** one correct cache-aside implementation everyone reuses, replacing the hand-rolled get/miss/set in `PermissionService`.

**Tasks:**
- Add a default-implemented `GetOrCreateAsync<T>` to `ICacheService`:
  ```csharp
  Task<T> GetOrCreateAsync<T>(
      string key,
      Func<CancellationToken, Task<T>> factory,
      TimeSpan? expiration = null,
      CancellationToken cancellationToken = default);
  ```
  Semantics: return cached hit; on miss run `factory`, `SetAsync` the result, return it. Do **not** cache when the factory yields `null`/`default` (avoid negative-caching surprises — revisit only if a stampede shows up).
- Add a `CacheOptions`/`CachingSettings` bound from config with a **default TTL** and a small **expiration jitter** (e.g. ±10%) so mass-inserted keys don't all expire on the same tick (cache-stampede mitigation) — a concrete, reviewable piece of "cache engineering" for the portfolio story.
- Add a tiny `CacheKeys` convention helper (namespaced `"{area}:{entity}:{id}"`) so keys are built in one place, not string-concatenated at call sites.
- **Refactor `PermissionService`** to call `GetOrCreateAsync(CreateCacheKey(identityId), ct => …rpc…, TimeSpan.FromMinutes(5))` — proves the helper on the one existing consumer and shrinks that class. Behavior must stay identical (still no negative caching of the RPC failure path).

**Unit tests** (`Common` test project — add one if none exists for infrastructure, else a focused `CacheServiceTests`):
- hit → factory **not** invoked, cached value returned;
- miss → factory invoked once, value stored then returned;
- `null` factory result → not stored;
- jitter stays within the configured band.
(Use a fake/in-memory `IDistributedCache` — `AddDistributedMemoryCache` — so these stay pure unit tests.)

**Integration test** (reuse an existing suite with a live Redis container, e.g. Restaurants): round-trip a real value through `GetOrCreateAsync` against Redis; assert a second call served without re-running the factory; assert TTL expiry evicts.

**Done when:** `PermissionService` is on the shared helper, all existing permission/auth integration tests stay green, new cache tests pass.

---

## 3. Milestone B — Declarative query caching + menu reads

**Goal:** cache the read-heavy Restaurant queries with zero caching code inside their handlers.

**Tasks:**
- In `Common.Application`, add a marker:
  ```csharp
  public interface ICachedQuery { string CacheKey { get; } TimeSpan? Expiration { get; } }
  public interface ICachedQuery<TResponse> : IQuery<TResponse>, ICachedQuery;
  ```
- In `Common.Infrastructure` (or `Common.Application/Behaviors` alongside the others), add `QueryCachingBehavior<TRequest, TResponse>` registered **after** validation in the pipeline: if `TRequest is ICachedQuery`, delegate to `ICacheService.GetOrCreateAsync(request.CacheKey, _ => next(), request.Expiration)`. Only cache successful `Result<T>` — never cache a `Result.Failure` (so a transient miss/NotFound isn't pinned).
- Opt the three read queries in (handlers untouched — still one Dapper read):
  - `GetMenuQuery` → key `restaurants:menu:{RestaurantId}`, TTL from config (default 5 min, per the project plan);
  - `GetRestaurantQuery` → key `restaurants:detail:{RestaurantId}`;
  - `GetMenuItemQuery` → key `restaurants:item:{MenuItemId}`.
  Keys are produced via the `RestaurantCacheKeys` helper (shared with Milestone C so read and invalidate can't drift).
- Register the behavior in the DI where the other behaviors are registered; confirm ordering (Exception → Logging → Validation → **Caching** → handler).

**Unit tests** (`Restaurants.UnitTests` or a `Common` behavior test):
- behavior returns cached value on hit without invoking `next`;
- miss invokes `next`, stores, returns;
- `Result.Failure` from `next` is **not** cached;
- non-`ICachedQuery` request passes straight through.

**Integration test** (Restaurants suite, real Redis + Postgres):
- `GET restaurants/{id}/menu` twice → second served from cache. Prove it by mutating the row **directly in Postgres** between calls and asserting the *stale* (cached) menu still comes back within TTL — this is the unambiguous "it's actually cached" assertion, and it sets up Milestone C's inverse test.

**Done when:** menu/detail/item reads are cache-backed, existing Restaurants integration tests green, new caching tests pass. **Shipped 2026-07-25** — `ICachedQuery`/`QueryCachingBehavior` landed as specified above, but review of the existing Restaurants integration suite turned up a real regression: `GetRestaurantQuery`/`GetMenuItemQuery` aren't purely public reads — `RestaurantRegisteredDomainEventHandler`, `RestaurantAddressUpdatedDomainEventHandler`, `MenuItemAddedDomainEventHandler`, `MenuItemUpdatedDomainEventHandler`, and `MenuItemPriceChangedDomainEventHandler` all call the same cached queries via `ISender` to build their integration-event snapshot after a write. Once cached, a later write's snapshot read could come back stale and publish a stale integration event (`UpdateRestaurantTests.Should_UpdateDetailsAndAddress_WhenRequestIsValid` and `MenuTests.MenuItemLifecycle_Should_PropagateToOrdersReplica` both failed this way before the fix). **Resolution:** `restaurants:detail:{id}` and `restaurants:item:{id}` are now evicted inline — `cacheService.RemoveAsync(...)` right after `SaveChangesAsync` in `UpdateRestaurantCommandHandler`, `UpdateMenuItemCommandHandler`, and `SetMenuItemAvailabilityCommandHandler` — i.e. Milestone C's own "near-instant eviction" fallback (see below), pulled forward early because outbox-lag invalidation wasn't just suboptimal here, it was wrong. `OnboardRestaurant`/`CreateMenuItem` need no eviction (new ids, nothing cached yet to invalidate). `restaurants:menu:{id}` has no such internal reader and still has **no invalidation at all** (TTL-only) — that's the entire remaining scope of Milestone C below.

---

## 4. Milestone C — Menu cache invalidation

**Goal:** a menu edit makes the next read fresh, without waiting for TTL.

**Narrowed scope (post-Milestone B):** `restaurants:detail:{id}` and `restaurants:item:{id}` are already invalidated — inline, synchronously, in `UpdateRestaurantCommandHandler` / `UpdateMenuItemCommandHandler` / `SetMenuItemAvailabilityCommandHandler` (shipped as part of B, see above; not open work here). The only key left with no invalidation is **`restaurants:menu:{id}`** — it's currently TTL-only (5 min, `RestaurantCacheKeys.Expiration`). That's the sole target of this milestone.

**Decision taken (2026-07-27): (a) — inline eviction.** `cacheService.RemoveAsync(RestaurantCacheKeys.Menu(restaurantId))` now runs right after `SaveChangesAsync` in `CreateMenuItemCommandHandler`, `UpdateMenuItemCommandHandler`, `SetMenuItemAvailabilityCommandHandler`, `CreateMenuCategoryCommandHandler`, `UpdateMenuCategoryCommandHandler`. Rationale: it keeps exactly **one** invalidation strategy across the whole feature (B already shipped inline eviction for `detail`/`item`; mixing in outbox-driven eviction for `menu` would mean two models to reason about for one cache), and it actually delivers this milestone's stated goal — *the next* read is fresh, not "fresh within one `ProcessOutboxJob` tick". `CreateMenuItem`/`CreateMenuCategory` gained `ICacheService` as a constructor dependency; `UpdateMenuItem`/`SetMenuItemAvailability` already had it from B and now evict two keys (their item key **and** the composed menu that embeds it). The 5-minute TTL stays as the safety net for the crash-between-save-and-evict window.

**(b) — outbox-driven `DomainEventHandler`s — was rejected**, not because it was incorrect (unlike detail/item, nothing reads `restaurants:menu:{id}` internally to build a snapshot, so B's bug could not recur here) but because the dispatch lag is a real freshness regression for zero architectural gain over (a), and it would have left the feature with two invalidation mechanisms.

Both paths were required to use `RestaurantCacheKeys.Menu(restaurantId)` (shared with B) as the single key-construction source; (a) does.

**Unit tests** (`Restaurants.UnitTests` → `Restaurants/MenuCacheInvalidationTests.cs`, 6 tests): each of the five command handlers is driven directly against a recording `ICacheService` and asserts the exact key set evicted — menu-only for `CreateMenuCategory`/`UpdateMenuCategory`/`CreateMenuItem`, item **+** menu for `UpdateMenuItem`/`SetMenuItemAvailability` — plus a negative case proving a failed command (restaurant not found) never evicts and never saves. This required a first-time `Application` project reference + `InternalsVisibleTo` on `Restaurants.Application` (the handlers are `internal sealed`); dependencies are hand-rolled fakes in `Abstractions/Fakes.cs` since the repo carries no mocking library.

**Integration test** (`Restaurants.IntegrationTests/Caching/MenuInvalidationTests.cs`, 5 tests — the inverse of B's `MenuCachingTests`): warm the menu cache → mutate through the **real endpoint** → read again and assert the **new** value, immediately, with no polling. Covers reprice, sell-out, item added, category renamed, category added. Verified non-vacuous: removing the menu evict from `UpdateMenuItemCommandHandler` makes the reprice test fail.

**Done — shipped 2026-07-27.** A real menu edit (item or category) is reflected on the next `GET restaurants/{id}/menu`; build clean, Restaurants unit 45/45 and integration 35/35 green. **Feature 2.3 is now complete through C; only Milestone D (distributed lock) remains for the feature proper.**

---

## 5. Milestone D — Distributed lock for driver assignment (Delivery)

**Goal:** never offer one delivery twice concurrently, and never hand the same driver two orders at once.

**The race (today):** `DeliveryAssignmentService.OfferNextAsync` guards re-entry with `if (delivery.Status != Pending) return;`, but that's a check-then-act on a value two callers can both read as `Pending` before either saves — and two *different* `Pending` deliveries can independently pick the **same** nearest available driver in the same instant. Triggers that overlap in practice: a `RejectDeliveryOffer` re-offer, the periodic `ProcessExpiredOffersJob`, and a fresh `CreateDelivery`.

**Tasks:**
- Add `IDistributedLock` to `Common.Application` and a Redis implementation in `Common.Infrastructure`:
  ```csharp
  Task<IAsyncDisposable?> TryAcquireAsync(string resource, TimeSpan ttl, CancellationToken ct = default);
  ```
  Implement with StackExchange.Redis `SET resource token NX PX ttl` and a token-checked release (Lua `GET`+`DEL`) — single-node Redlock, no extra dependency. (Alternative for review: take the `RedLock.net` package; call it out in the PR and decide there. The abstraction is identical either way.)
- Wrap the assignment critical section: in `OfferNextAsync`, after loading the delivery and **before** selecting/offering, acquire a lock on the **candidate driver** (`delivery:driver-lock:{driverId}`) once a candidate is chosen, and re-verify the driver is still available inside the lock; also guard the per-delivery transition with `delivery:offer-lock:{deliveryId}` so the two overlapping triggers serialize. Lock TTL comfortably exceeds the offer transaction, well under the offer window. On failed acquisition: skip (idempotent no-op) — the job/other trigger will retry.
- Keep the existing `Status != Pending` idempotency check — the lock and the check are complementary (lock prevents the race, the check prevents wasted work).
- **Note the raised stakes on Redis availability.** Since this plan was drafted, Delivery Phase 2 shipped driver location as a *live* Redis GEO store (`delivery:drivers:available` sorted set + per-driver hash in `RedisDriverLocationStore`) rather than the project plan's original Cosmos DB (now deferred to optional Milestone G). That means Redis is no longer just a cache for the assignment path — it's the **primary store** for "who's nearest," and this milestone adds a second hard dependency (the lock) on top of it. A Redis outage now breaks live driver assignment outright, not just degrades latency. Worth surfacing in review as an operational consideration, not a reason to change the design.

**Unit tests** (`Delivery.UnitTests` or `Common`): lock abstraction contract against a fake — second `TryAcquire` on a held resource returns `null`; release frees it; release only deletes the caller's own token (no cross-owner delete).

**Integration test** (Delivery suite, real Redis): fire two concurrent `OfferNextAsync` calls (or two ready deliveries whose only nearby driver is the same one) and assert exactly **one** offer is created / the driver is assigned to exactly one delivery. This is the "distributed locking prevents the double-assign" proof.

**Done when:** concurrent assignment converges to a single offer/assignment, existing Delivery assignment/expiry tests green.

**Done — shipped 2026-07-27.** `IDistributedLock` (`Common.Application/Locking`) with a Redis implementation (`SET NX PX` + Lua `GET`-compare-`DEL` release, no new package) and an `InMemoryDistributedLock` fallback registered on the same Redis-unreachable branch as the existing in-memory cache. Keys/TTL are built in one place — `DeliveryLocks` in `Delivery.Application/Abstractions/Assignment` (`delivery:offer-lock:{deliveryId}`, `delivery:driver-lock:{driverId}`, 5 s TTL) — so the offer routine and the accept handler can't drift.

Three deviations from the tasks above, all deliberate:

- **The offer lock is taken *before* the delivery is loaded, not after.** The check-then-act this protects begins at the read; a lock acquired after it would still let both callers act on the same `Pending` snapshot.
- **A failed acquisition returns `Result.Failure(DeliveryErrors.AssignmentInProgress)`, not a silent success.** "Skip as an idempotent no-op" would leave the delivery `Pending` with nothing to re-drive it — `ProcessExpiredOffersJob` only scans `Offered` rows, so a skipped delivery is stranded, not retried. A failure at least propagates to the caller that can retry (inbox row records the error, the next job tick re-finds the still-expired offer, the driver re-sends their rejection). Driver-lock contention doesn't fail the routine at all: it moves to the next candidate and only fails if *every* candidate was locked or had gone unavailable — and never parks the delivery `Unassigned` on that path, since that outcome waits on a human.
- **The accept path is locked too** (the milestone table's "wrap `OfferNextAsync`/accept`"). `AcceptDeliveryOfferCommandHandler`'s `Available → Busy` reservation is a check-then-act across two transactions with no concurrency token on the row, so a driver holding two open offers who accepts both at once genuinely gets both. Verified: with the lock disabled the new test sees two `204`s and one driver on two deliveries.

**Residual gap (documented in the service, not fixed here):** callers that stage state on the aggregate before calling in (`RejectDeliveryOffer`, `ExpireDeliveryOffer`) load the delivery *outside* the lock, and EF's identity map hands that same tracked instance back inside it. The lock removes the double-offer; a loser's stale snapshot can still overwrite the winner's row. Closing that needs an optimistic concurrency token (`UseXminAsConcurrencyToken`) on `deliveries` — a separate, broader change.

**Tests:** `Common.UnitTests/Locking/DistributedLockTests.cs` (6 — contend, per-resource independence, release, idempotent dispose, TTL takeover + no cross-owner delete, 20-way race admits one) and `Delivery.IntegrationTests/Deliveries/AssignmentLockTests.cs` (4 — the Redis lock contract incl. the token check, TTL lapse, 4 concurrent offer routines → exactly one `DeliveryOfferedDomainEvent`, 2 concurrent accepts → exactly one assignment). Both race tests were verified non-vacuous: with locking disabled they fail with "found 4" offers and "found 2" accepted. Build clean; Delivery integration 42/42, Delivery unit 68/68, Common unit 16/16, Restaurants integration 35/35.

**Feature 2.3 is complete through D** — only the optional E (observability) and F (Azure Cache for Redis) remain. **Both have since shipped: E on 2026-07-29 (§6) and F on 2026-07-29 (§7).**

---

## 6. Milestone E — Cache observability (optional, small)

**Goal:** make cache effectiveness visible — hit rate is the number that justifies the whole feature.

**Tasks:** emit OpenTelemetry counters (`cache.hits`, `cache.misses`, tagged by key prefix) and a debug-level structured log from `QueryCachingBehavior`/`CacheService`. **Correction:** OTel is only wired for *tracing* in `AddInfrastructure` today — the metrics pillar (`.WithMetrics()`, a meter provider, any exporter) does not exist yet; that's `TELEMETRY_PHASE2_PLAN.md` Milestone A. Emitting these counters now means they exist but are **not visible anywhere** until Telemetry 2.4 lands, and Telemetry's own §11 (renumbered from §10 when 2.4 added Milestone G) already plans to detect and absorb/centralise any one-off `.WithMetrics()` this milestone might add. Given that, **prefer skipping this milestone entirely and letting Telemetry 2.4 Milestone A add the cache counters directly** (it already names them `cache.hits`/`cache.misses` and builds its Grafana hit-rate panel from them) — don't stand up a throwaway metrics pipeline just to tear it down a plan later. If built anyway, keep it to counter emission only, explicitly flagged as invisible until 2.4 ships.

**Tests:** a unit test asserting the counter increments on hit vs. miss. No integration test needed.

**Done when:** hit/miss metrics show up in the existing telemetry pipeline (post-2.4) or — if skipped — the counters are left for Telemetry 2.4 to add.

**Done — shipped 2026-07-29, built despite the "prefer skipping" recommendation above (the counters were wanted now; the recommendation's substance was honoured by not standing up a pipeline).** *(This milestone was written up twice before any code existed — first dated 2026-07-28, then re-dated 2026-07-29 — and both times `CacheDiagnostics` was absent from the working tree and from every branch. This entry describes the build that finally landed and was verified green; the design calls below survived unchanged from those write-ups, only the test counts were wrong.)* Scope held to **counter emission only** — no `.WithMetrics()`, no meter provider, no exporter, no new package. `CacheDiagnostics` (`Common.Infrastructure/Caching`) owns a `Meter` named **`FoodDeliveryService.Cache`** with `cache.hits` / `cache.misses` (`Counter<long>`, unit `{lookup}`), each measurement tagged `cache.key_prefix`. **These measurements were collected by nothing until Telemetry 2.4 Milestone A shipped on 2026-07-31** — it added the metrics reader and the `AddMeter(CacheDiagnostics.MeterName)` line, and an Orders integration test now asserts the counters are exported by the host's own `MeterProvider`. Nothing for 2.4 §11 (then §10) to "absorb and centralise": there was no duplicate metrics wiring to remove, only a meter name to register. (2.4's reconciliation section was written a day earlier, against the tree where this milestone's code did not yet exist, and has since been corrected.) A Grafana hit-rate panel still waits on Telemetry Milestone E's backend.

Three decisions worth recording:

- **Instrumentation sits in `CacheService.GetAsync`, not in `QueryCachingBehavior`.** That method is the one choke point every read passes through — the behavior's cached queries, `GetOrCreateAsync` (and therefore `PermissionService`), and any direct `GetAsync`. Instrumenting both places would double-count every behavior lookup; instrumenting only the behavior would leave the permission cache — the oldest and busiest consumer — unmeasured. The milestone's "behavior/CacheService" is therefore satisfied at `CacheService` alone, and the debug-level structured log (`"Cache hit/miss for {CacheKey}"`, full key — logs have no cardinality budget) sits in the same spot rather than emitting a second line per lookup from the behavior. `CacheService` gained `ILogger<CacheService>` as a constructor dependency.
- **The tag is the key *prefix*, not the key.** New `CacheKeys.Prefix(key)` drops the trailing id segment — `restaurants:menu:{guid}` → `restaurants:menu`, `user_permissions:{identityId}` → `user_permissions` — so hit rate is readable per cached surface without one time series per restaurant or user. It lives next to `CacheKeys.Create` because it is that convention's inverse: both `Create` overloads put the id last, which is exactly what makes dropping the last segment safe.
- **No metrics-testing package.** Assertions use the BCL `MeterListener` rather than `MetricCollector<T>` from `Microsoft.Extensions.Diagnostics.Testing` — Telemetry 2.4 §10 (its testing summary) already plans to bring that package in, and one milestone of counter emission doesn't justify pulling it forward.

**Tests:** `Common.UnitTests/Caching/CacheDiagnosticsTests.cs` (5 — miss on cold key, hit on warm key, `GetOrCreateAsync` recording miss-then-hit across two calls, the same through `QueryCachingBehavior`, and the id staying out of the tag). The counters are process-wide statics and xUnit runs test classes in parallel, so each test filters measurements by its own unique key prefix — that's what keeps the assertions immune to other suites. Verified non-vacuous by temporarily deleting the `RecordMiss` call — 4 of the 5 fail, the hit-only test correctly unaffected. Solution build clean (0 warnings under `TreatWarningsAsErrors`); Common unit **21/21** (was 16), Restaurants caching integration 8/8 green (they exercise the real host's `CacheService` construction, now with the added logger dependency). The two existing `Common.UnitTests` fixtures that build a `CacheService` by hand needed `.AddLogging()` for the new constructor parameter.

---

## 7. Milestone F — Azure Cache for Redis + hardening (optional, small)

**Goal:** the Azure/portfolio story, behind the same `IDistributedCache`.

**Tasks:** support an Azure Cache for Redis connection string (TLS, `abortConnect=false`, resilient reconnection) selected by environment; confirm a Redis health check is reported (add if missing); short doc/README note on cache keys, TTLs, and the invalidation model. No code path changes — same `ICacheService`, same behavior, same lock. **This migration is no longer "just the cache tier"** — it also carries Delivery's live GEO driver-location store and the Milestone D lock (see the note there), so treat availability/failover requirements accordingly, not as a pure cache-hit-rate concern.

**Tests:** covered by existing suites against the local container; document the manual Azure smoke check.

**Done when:** the app runs against Azure Cache for Redis with no code change beyond configuration.

**Done — shipped 2026-07-29.** An Azure connection string is now the *only* thing that changes: `RedisConnectionOptions.Create` (`Common.Infrastructure/Caching`) parses `ConnectionStrings:Cache` and applies two hardening defaults before anything connects — `AbortOnConnectFail = false` and an exponential (1 s → 30 s) reconnect back-off in place of StackExchange.Redis' linear default. Everything else (endpoint, credential, TLS, timeouts, retry counts) stays the connection string's business, so environments differ by configuration alone. The docs are `docs/caching.md`: keys, TTLs, the invalidation model, the four *disjoint* things riding this one Redis, the Azure provisioning/wiring steps and an 8-step manual smoke check.

Four things turned out differently from the tasks above:

- **No TLS code was needed.** StackExchange.Redis already recognises the Azure Cache DNS suffixes (`*.redis.cache.windows.net`, `*.redis.azure.net`) and turns `Ssl` on itself — verified, then the hand-written inference was deleted rather than kept as dead code that looks load-bearing. Two characterization tests pin the behaviour so a future edit of `Create` can't silently drop it.
- **`AbortOnConnectFail = false` is forced, not defaulted** (a connection string asking for `true` is overridden). It is what makes `Connect` return a usable, self-reconnecting multiplexer against a server that is down, and the whole startup path now depends on that: `Connect` throws *only* for an unusable connection string, which is a misconfiguration worth failing on.
- **The silent in-memory fallback became an explicit, environment-selected decision** — the one genuine hardening find. The old `try { redis } catch { AddDistributedMemoryCache(); InMemoryDistributedLock }` swallowed everything, and in production that trades a *distributed* lock for a process-local one that stops protecting driver assignment the moment a second replica exists (and it left `IConnectionMultiplexer` unregistered, so Delivery's GEO store and Real-Time's subscriber failed *DI resolution* rather than degrading). Now: the multiplexer is always registered; `AddInfrastructure` takes `allowInMemoryCacheFallback`, which every host passes as `builder.Environment.IsDevelopment()`; outside development an unreachable Redis keeps the reconnecting connection and lets the health check report unhealthy instead. In development the fallback still applies and announces itself with a `WARN` at boot (`InMemoryFallbackWarning`) rather than degrading in silence.
- **The health check was present but pointed at the wrong thing.** All six Redis-using hosts had `.AddRedis(redisConnectionString)`, which opens a *second* connection with default options — it could report healthy on settings the app doesn't use. They now pass `sp => sp.GetRequiredService<IConnectionMultiplexer>()`, probing the very connection the cache, the lock and the GEO store share. The SignalR backplane is the one component that must own its connection, so Real-Time hands it the same `RedisConnectionOptions.Create(...)` result instead of the raw string.

**Tests:** `Common.UnitTests/Caching/RedisConnectionOptionsTests.cs` (11 — abort-on-connect-fail forced both ways, exponential policy, client name, TLS inherited for both Azure suffixes, no TLS locally, connection-string tuning and the full Azure string preserved, empty string throws). Verified non-vacuous by inverting the two defaults: the abort-on-connect-fail pair fails, the TLS pair correctly does not (it pins the library, not us). Build clean (0 warnings under `TreatWarningsAsErrors`); Common unit **32/32** (was 21), Restaurants integration 35/35, Delivery integration 42/42 (lock + GEO store over the re-registered multiplexer), RealTime integration 17/17 (backplane on `ConfigurationOptions`). Azure itself is unverified by construction — no subscription is in play here; §5 of `docs/caching.md` is the manual check that closes that gap.

**Feature 2.3 is complete: A–F all shipped.**

---

## 8. Out of scope / explicitly deferred

- **`GetRestaurants` list/search caching** — paged + multi-filter → many key permutations and broad invalidation fan-out; low hit rate. Revisit only if load testing (Feature 3.5) shows it's hot.
- **Session caching** — the project plan lists it, but auth is **stateless Duende JWTs**; there is no server session to cache. The nearest real thing — permission caching — already exists (Milestone A refactors it). Noted as N/A, not skipped silently.
- **Rate-limiting counters in Redis** — the project plan lists it under caching, but rate limiting is a **Gateway (Feature 1.3)** concern; not moved here.
- **Cross-service cache coordination / pub-sub eviction** — single Redis instance per environment; each service owns its own keys. Not needed at this scale.
- **Restaurant rating-aggregate caching** — the project plan's "Redis (cached aggregates)" bullet under Feature 2.6 (Reviews). `REVIEWS_PHASE2_PLAN.md` now exists and explicitly reuses this plan's `GetOrCreateAsync` helper (Milestone A) for the per-restaurant average once that service ships — nothing to build here.

---

## 9. Testing summary

| Milestone | Unit | Integration (real Redis) |
|---|---|---|
| A | `GetOrCreateAsync` hit/miss/null/jitter | round-trip + TTL evict; permission flow still green |
| B | behavior hit/miss/failure-not-cached/passthrough | menu served from cache while Postgres row diverges |
| C ✅ | each command handler removes exact keys (6) | edit → next read is fresh (5, headline test) |
| D ✅ | lock contract (contend/release/own-token/TTL takeover) (6) | lock contract on real Redis + concurrent offer → single offer, concurrent accept → single assignment (4) |
| E ✅ | counter increments on hit vs miss, prefix tag carries no id (5) | — |
| F ✅ | hardened connection options: forced defaults, inherited TLS, preserved tuning (11) | existing suites (Restaurants 35, Delivery 42, RealTime 17); manual Azure smoke documented in `docs/caching.md` |

Every milestone leaves the build green and ships behind its own PR. The feature is **complete after A–D**; E and F are portfolio polish.

---

## 10. Reconciliation with the Real-Time plan (Feature 2.2)

This plan runs **after** `REALTIME_PHASE2_PLAN.md`. Both features lean on Redis, so this section pins down that they do **not** overlap — nothing here re-implements anything the Real-Time service already built.

**Redis is used for disjoint purposes; no key or channel collides.**

| Redis concern | Owned by | Keys / channels |
|---|---|---|
| SignalR backplane | Real-Time (2.2) | SignalR-internal |
| Driver-location **pub/sub** stream | Real-Time (2.2) — Delivery `PUBLISH`es, Real-Time subscribes | channel `delivery:driver-locations` |
| Ephemeral order/driver routing map | Real-Time (2.2) | `rt:order:{id}`, `rt:driver:{id}` |
| Cache-aside reads + invalidation | **this plan (B, C)** | `restaurants:menu:{id}`, `restaurants:detail:{id}`, `restaurants:item:{id}` |
| Permission cache | already shipped; **refactored here (A)** | `user_permissions:{identityId}` |
| Distributed lock (assignment) | **this plan (D)** | `delivery:driver-lock:{driverId}`, `delivery:offer-lock:{deliveryId}` |

**Both plans modify the Delivery service — but in disjoint files.** Real-Time (Milestone C) adds a one-line `PUBLISH` to the *location* handler (`RecordDriverLocationCommandHandler`); this plan (Milestone D) wraps the *assignment* routine (`DeliveryAssignmentService.OfferNextAsync`). No shared file, no merge conflict.

**This plan's lock strengthens Real-Time, it doesn't duplicate it.** Real-Time binds `rt:driver:{driverId} → {orderId, customerId}` off `DriverAssignedIntegrationEvent`. The double-assign race that Milestone D closes is exactly what could overwrite that binding with a second order. Milestone D therefore removes a latent correctness hole in the already-shipped Real-Time path — it does not touch Real-Time code.

**No competing pub/sub invalidation.** Cross-instance, pub/sub-based cache eviction is explicitly **out of scope** (§8); invalidation here is domain-event-driven `RemoveAsync`. So this plan introduces **no** new Redis pub/sub scheme alongside Real-Time's `delivery:driver-locations` channel.

**The optional Azure milestones are complementary, not competing.** Real-Time's optional Azure step is **Azure SignalR Service** (offloads the socket backplane); this plan's optional Milestone F is **Azure Cache for Redis** (backs the cache, lock, and — if kept on Redis — the location pub/sub). If both are adopted they land on different managed services by design; neither milestone assumes the other.
