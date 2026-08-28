# CAP, consistency and failure behaviour

> How the nine services trade consistency against availability, where each choice was made
> deliberately, and what actually happens when a given dependency goes away.

## 1. The short version

The system is **AP by default, with three deliberate CP exceptions**.

The default is set by one structural decision: every service owns its own schema and never reads
another service's tables. Cross-service state moves as integration events through a transactional
outbox → RabbitMQ → inbox. That is an availability-first choice, made once, and it is why almost
every module keeps serving traffic while a peer is down.

CAP only bites during a partition, so the more useful frame for most of what follows is **PACELC**:
what was actually chosen in the *normal* case is latency over consistency (local replicas, cached
reads) — the "ELC" half of the acronym.

## 2. What is designed as what

| Mechanism | Where | C/A choice | Consistency window |
|---|---|---|---|
| Aggregate + `SaveChangesAsync` | every module | Strong (single Postgres node — CAP does not apply inside one transaction) | immediate |
| Outbox → RabbitMQ → inbox → replica | Orders↔Restaurants, Delivery↔Orders, Notifications, RealTime, FraudDetection | **AP** | one Quartz outbox tick + broker + inbox tick |
| Replica tables (`Restaurant`, `User`, `RestaurantManager` copies) | consuming modules | **AP** — read local, never call the owner | as above |
| `PermissionService` RPC | all 7 API services → Users | **CP** — the request fails if Users is unreachable *and* the cache is cold | 5 min TTL |
| Redis cache-aside (`QueryCachingBehavior`) | Restaurants reads | **AP** — degrades to Postgres | ≤5 min TTL, but inline eviction after writes makes the next read fresh |
| `IDistributedLock` (Redis `SET NX PX`) | Delivery assignment / accept | **CP** — returns `AssignmentInProgress` rather than double-book | immediate |
| Redis GEO driver positions | Delivery assignment | **CP by accident** — no other store, so it is a hard dependency, not a cache | immediate |

The lock row is the one worth naming out loud:
[`DeliveryAssignmentService.cs:59`](../src/Modules/Delivery/FoodDeliveryService.Modules.Delivery.Infrastructure/Assignment/DeliveryAssignmentService.cs)
explicitly refuses to operate rather than risk assigning one driver twice. Double-booking a driver
is a real-world cost that no compensating action fixes cleanly, so consistency wins. Everywhere
else a stale menu or a late notification is cheap, and availability wins.

## 3. Behaviour under specific failures

### Users service down

Already-authenticated traffic keeps working for up to 5 minutes on the cached permissions; once the
TTL expires, **every** service starts failing authenticated requests. This is the largest
availability coupling in the system — seven services share one CP dependency, and the cache is the
only thing between a Users outage and a full-system outage.

`PermissionService` already flags this in its own XML doc ("the system's only synchronous
cross-service call; prefer replicating data via integration events before adding another"). The AP
fix would be replicating permissions as a projection, the way `RestaurantManager` was replicated
into RealTime.

### Restaurants down

Orders keeps accepting orders — it reads its own restaurant/menu replica. The design working as
intended: an order accepted against a menu item that was just deleted is resolved after the fact,
not prevented. Consistency is restored when the event drains.

### RabbitMQ down or partitioned

Nothing user-facing fails. Commands still commit, because the outbox row commits in the *same*
transaction as the aggregate. Events accumulate in `outbox_messages` and drain when the broker
returns. This is the cleanest AP property in the system — the partition is absorbed by durable
storage rather than surfaced to the caller.

Visible symptom is lag, not error: order status stops moving to `OutForDelivery`, confirmation
emails go out late, fraud projections go stale.

### Redis down

Split by concern (see [caching.md](caching.md) §1):

- **Reads degrade** — cache-aside and the permission cache fall through to Postgres / the bus.
  Slower, still correct.
- **Driver assignment stops** — both because the lock cannot be acquired and because the GEO set is
  the only store of live driver positions.

Redis is not a cache in this topology; for one bounded context it is a system of record.

### Postgres down

Total outage. All nine services share one instance in `docker-compose.yml` — separate schemas, not
separate clusters. Logical isolation without physical isolation. Adequate for the current stage; the
honest statement is "schema-per-service, ready to split, not yet split."

### Two replicas of the same service

The one real gap, already caught by `KUBERNETES_PHASE2_PLAN.md` Milestone D. `ProcessOutboxJob` uses
`FOR UPDATE` plus `[DisallowConcurrentExecution]`, which only guards *within* a process. Two pods
polling the same outbox contend: `FOR UPDATE` blocks rather than skips, giving serialization and
stalls rather than duplicates. `FOR UPDATE SKIP LOCKED` is the fix; it is why RealTime is pinned to
a single replica today.

## 4. Overall shape

Reads are eventually consistent. Writes are strongly consistent within an aggregate. No distributed
transaction is ever taken — no 2PC, no saga-with-rollback. Instead:

- idempotent consumers (inbox dedup),
- compensating domain actions (order cancellation),
- business invariants enforced inside a single aggregate boundary.

That is the standard microservices answer to CAP. The two deviations from it — the permissions RPC
and Redis-as-system-of-record — are each traceable to a specific reason rather than to drift.
