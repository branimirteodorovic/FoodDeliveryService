# Feature 2.2 — Real-Time Order & Driver Tracking (SignalR) — Implementation Plan

> Fifth module implementation plan, after `RESTAURANTS_PHASE1_PLAN.md`, `ORDERS_PHASE1_PLAN.md`, `NOTIFICATIONS_PHASE1_PLAN.md` and `DELIVERY_PHASE2_PLAN.md`. This one covers **Feature 2.2 — Real-Time Order & Driver Tracking (SignalR)**, the second feature of **Phase 2** in `FoodDelivery_ProjectPlan.md`.

> **Scope for this iteration:** a **new Real-Time service** that holds authenticated SignalR connections and fans out *ephemeral* updates to the right screens: the customer's live order-status timeline and moving driver pin, the restaurant dashboard's new-order/status feed, and the support dashboard's live activity feed. It **consumes the lifecycle integration events that already exist** (Orders + Delivery, both shipped) and forwards them to SignalR groups; it receives the high-frequency **driver location** stream over Redis pub/sub (that stream is deliberately off the bus). Redis is the SignalR backplane. No new business state, no new domain rules — this is a transport/fan-out layer over events other services already own.

Decisions locked in for this plan:

- **Real-time push lives in a new, dedicated service** — `fooddeliveryservice.realtime.api` on `:5600`, routed at a new gateway prefix `hubs/**`. It is **not** folded into Notifications. Notifications owns *durable, auditable* delivery (the one confirmation email, and later mobile push) — each send is a logged `Notification` row. Real-time fan-out is the opposite: **ephemeral, best-effort, high-fan-out over persistent sockets**, with a completely different scaling profile (a Redis backplane, WebSocket connection management). Mixing the two would put a socket hub inside an audit-logging module. The Notifications `Realtime` channel enum (`NOTIFICATIONS_PHASE1_PLAN.md` §7) is **superseded** by this service — see §11.
- **The socket is best-effort; the REST read models are the source of truth.** A dropped frame is never a correctness problem: on connect and on every reconnect the client re-fetches authoritative state from `GET orders/{id}` and `GET delivery/orders/{orderId}/delivery` (both shipped), then lets the socket stream deltas. This single decision is what lets the rest of the design be lightweight — no durable inbox, no per-frame persistence, no replay.
- **Status updates ride direct MassTransit consumers, not the durable inbox.** The Real-Time service consumes the existing lifecycle integration events with plain `IConsumer<T>` implementations that broadcast to SignalR **immediately** — it does **not** use the `IntegrationEventConsumer<T>` inbox path the other modules use. This is a **deliberate, documented departure** from the inbox rule (presentation-messaging rules), justified by the bullet above: durability belongs to the DB and the REST re-sync, not to a transient socket frame, and the inbox's Quartz poll interval would add pointless latency to a "real-time" feature. This exception is scoped to this service only.
- **Driver location comes over Redis pub/sub, never the bus.** `POST delivery/drivers/me/location` is the highest-traffic endpoint in the system and bypasses the aggregate/outbox by design (`DELIVERY_PHASE2_PLAN.md` §4.5). The Delivery location handler adds one fire-and-forget `PUBLISH` to a Redis channel after its existing GEO write (Redis is already on that hot path); the Real-Time service subscribes and forwards. RabbitMQ never sees a location frame.
- **Redis is the SignalR backplane.** Already in the stack and in `docker-compose`/Testcontainers, so scale-out across multiple Real-Time instances works and is testable offline. **Azure SignalR Service** is the managed swap, deferred to optional Milestone E — the portfolio Azure story, behind the same hub.
- **Every connection is authenticated by the same Duende JWT.** The browser WebSocket handshake can't set an `Authorization` header, so SignalR sends the token as the `access_token` query-string parameter; a JwtBearer `OnMessageReceived` hook reads it for `hubs/*` paths. The gateway forwards the WebSocket upgrade (YARP does this natively) on an authenticated `hubs/**` route.
- **A client is only ever placed in groups it is entitled to.** Group membership is derived from the JWT's own claims (`sub`, role), never from a client-supplied id. A customer lands in exactly `user:{sub}`; a restaurant manager in `restaurant:{their restaurantId}`; a support agent in `support`. The hub never trusts a userId/restaurantId sent by the caller.
- Reference implementations to mirror: **Orders/Delivery** for the host bootstrap + `AddInfrastructure` wiring, the **Restaurants integration suite** (`IntegrationTestWebAppFactory` + `UsersApiTestFactory` + real Duende JWTs) for driving authenticated flows end-to-end, and **Notifications** for the "pure consumer, minimal replica" shape.

---

## 0. Dependencies

**Fully unblocked — everything this plan consumes already exists on the bus today.**

| Needed | Source | Status |
|---|---|---|
| `OrderPlaced/Accepted/Rejected/ReadyForPickup/Cancelled` (carry `CustomerId`, `RestaurantId`) | Orders (Phase 1 D) | **shipped** |
| `DeliveryOffered`, `DriverAssigned`, `OrderPickedUp`, `OrderDelivered`, `DeliveryOfferRejected`, `DeliveryUnassigned` (carry `OrderId`/`DriverId`) | Delivery (Feature 2.1) | **shipped** |
| Driver location writes on the Redis hot path | Delivery `RecordDriverLocationCommand` | **shipped** — needs a one-line `PUBLISH` added (Milestone C) |
| REST re-sync read models (`GET orders/{id}`, `GET delivery/orders/{orderId}/delivery`) | Orders / Delivery | **shipped** |

> **No new integration events are required.** The two "final" statuses (out-for-delivery, delivered) are reconstructable from Delivery's `OrderPickedUp`/`OrderDelivered` events, so Orders does **not** need to add `OrderOutForDelivery`/`OrderDelivered` integration events for this feature. The Real-Time service subscribes to both event sources and maps them to a single client-facing status timeline.

---

## 1. Architecture overview

| Module / Service | Responsibility this iteration |
|---|---|
| **RealTime** (`fooddeliveryservice.realtime.api`) — **new** | Hosts `TrackingHub`. Authenticates connections (Duende JWT via `access_token`). Consumes Orders + Delivery lifecycle events (direct consumers) and forwards them as status frames to the right groups. Subscribes to the Redis driver-location channel and forwards positions to the tracking customer. Keeps a tiny **order→customer/restaurant routing map in Redis** (ephemeral), and — only from Milestone D — a minimal `RestaurantManager` Postgres replica for dashboard group resolution. Uses the Redis SignalR backplane. |
| **Delivery** (`fooddeliveryservice_delivery`) | One additive change (Milestone C): after the existing Redis GEO write, the location handler `PUBLISH`es `{driverId, lat, lon, recordedOnUtc}` to a Redis channel. No domain change, no bus traffic. |
| **Gateway** | One new `hubs/**` route + `fooddeliveryservice-realtime-cluster`, `AuthorizationPolicy: "default"`, WebSocket upgrade forwarded (YARP native). No anonymous route. |
| Orders / Restaurants / Users | **No work here.** All required events already publish. (Restaurants' registered/updated events feed the Milestone D replica, but Restaurants itself is untouched.) |

**Client-facing contract (stable hub surface).** Server→client methods on `TrackingHub`:
- `OrderStatusChanged(OrderStatusFrame)` — `{ orderId, status, occurredOnUtc, driverName?, driverVehicle? }`
- `DriverLocationChanged(DriverLocationFrame)` — `{ orderId, driverId, latitude, longitude, recordedOnUtc }`
- `RestaurantActivity(RestaurantActivityFrame)` — dashboard feed (new order, status change) for a restaurant
- `SupportActivity(SupportActivityFrame)` — global live activity for support

These DTO shapes are the public API of the feature; keep them additive-only after they land.

**End-to-end flow (customer tracking one order)**

1. Customer's app opens a WS to `wss://gateway/hubs/tracking?access_token=<jwt>`. Gateway validates + forwards the upgrade; the hub validates again, and `OnConnectedAsync` joins the caller to `user:{sub}`.
2. Client immediately GETs `orders/{id}` + `delivery/orders/{orderId}/delivery` to paint the current state (source of truth).
3. As the order moves, Orders/Delivery publish their lifecycle events → the Real-Time consumers broadcast `OrderStatusChanged` to `user:{customerId}` → the timeline updates with no refresh.
4. When a driver is assigned, `DriverAssignedIntegrationEvent` binds `driver→order→customer` in the Real-Time Redis map. The driver's app streams `POST delivery/drivers/me/location`; Delivery `PUBLISH`es each position; the Real-Time subscriber resolves the customer from the binding and broadcasts `DriverLocationChanged` to `user:{customerId}` → the pin moves.
5. On `OrderDelivered`/`OrderCancelled`, the binding is cleared; later stray location frames for that driver are dropped (no active binding).
6. If the socket drops, SignalR auto-reconnects, `OnConnectedAsync` re-joins `user:{sub}`, and the client re-runs step 2 — no missed-update problem.

---

## 2. Milestone A — Real-Time service skeleton + authenticated hub + connection management

The foundational PR: a new host that accepts an authenticated socket and puts the caller in their own group. No event consumption yet — just the connection substrate, proven end-to-end.

### 2.1 Projects & host
- `src/API/FoodDeliveryService.RealTime.Api` — copy the **Orders** host bootstrap (`Program.cs`, `OpenTelemetry/DiagnosticsConfig.cs` with `ServiceName = "FoodDeliveryService.RealTime"`, `appsettings*.json`, `Dockerfile`, health checks). It needs `AddInfrastructure` for JWT auth + Redis + OTel + MassTransit, but **no DbContext yet** (added in Milestone D).
- A thin module assembly set under `src/Modules/RealTime/` — realistically just `Application` (fan-out services, the Redis routing map abstraction) + `Infrastructure` (`RealTimeModule.cs`, Redis wiring, consumer registration) + `Presentation` (the hub + any REST endpoint). **No Domain, no IntegrationEvents project** — this service raises no domain events and publishes no contracts; it only consumes. Add the projects + the two test projects to `FoodDeliveryService.Api.slnx`.
- `docker-compose.yml`: `fooddeliveryservice.realtime.api` (`5600:8080`), same env block as Orders (`ConnectionStrings__Cache/Queue`, `Authentication`, OTLP, Seq) minus a `Database` for now.

### 2.2 SignalR + JWT + backplane wiring
- `AddSignalR().AddStackExchangeRedis(redisConnectionString)` — Redis backplane so any instance can reach any connection.
- JwtBearer `OnMessageReceived`: if the request path starts with `/hubs` and `access_token` is present in the query, set `context.Token` from it (the standard SignalR pattern). Otherwise auth is unchanged.
- `RequireAuthorization()` on the hub map, so an unauthenticated handshake is rejected at the negotiate step.

### 2.3 `TrackingHub` (Presentation)
```csharp
[Authorize]
internal sealed class TrackingHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        Guid userId = Context.User!.GetUserId();          // from sub claim — never from the client
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupNames.User(userId));
        await base.OnConnectedAsync();
    }
}
```
- `GroupNames` is a tiny static helper (`User(id) => $"user:{id}"`, `Restaurant(id)`, `Support`) so group naming is centralized and unit-testable.
- Reconnection is SignalR-native (the client uses `withAutomaticReconnect()`); each reconnect re-runs `OnConnectedAsync` and re-joins. Nothing server-side to persist.
- Map at `hubs/tracking`.

### 2.4 Gateway
Add `fooddeliveryservice-realtime-route1` → `hubs/{**catch-all}` (`AuthorizationPolicy: "default"`) and `fooddeliveryservice-realtime-cluster` → `http://fooddeliveryservice.realtime.api:8080`. YARP forwards WebSocket upgrades automatically; no extra transform needed. **No anonymous route.**

**Tests**
- *Unit* (`RealTime.UnitTests`): `GroupNames.User(id)` formatting; a `TrackingHub` with a mocked `HubCallerContext`/`IGroupManager` adds the connection to exactly `user:{sub}` and to nothing else; a principal missing the `sub` claim is rejected (no group join, throws/returns cleanly).
- *Integration* (`RealTime.IntegrationTests` — copy the Restaurants suite's `IntegrationTestWebAppFactory` + `UsersApiTestFactory` + `Poller`; use the real `HubConnectionBuilder` client against the in-process host): a client with a valid Duende JWT connects successfully; a client with **no** token, and one with an **invalid** token, both fail the handshake (401 at negotiate); after a forced drop, `withAutomaticReconnect` re-establishes and the caller is back in `user:{sub}` (assert via a Milestone-B broadcast, or a temporary hub echo method used only by the test).

---

## 3. Milestone B — Order status push to the customer

Turn the existing Orders lifecycle events into live timeline updates. This is the headline "status changes appear instantly" behaviour.

### 3.1 Direct consumers + fan-out service
- One MassTransit `IConsumer<T>` per Orders event (`OrderPlaced`, `OrderAccepted`, `OrderRejected`, `OrderReadyForPickup`, `OrderCancelled`), registered in `RealTimeModule.ConfigureConsumers` with `.Endpoint(c => c.InstanceId = instanceId)` (own queue). Each consumer is **thin**: it maps the event to an `OrderStatusFrame` and calls the fan-out service — it does **not** write an inbox row (documented departure, §Decisions).
- `IRealTimeNotifier` (Application abstraction) with `NotifyUserAsync(Guid userId, OrderStatusFrame frame, ct)`, implemented in Infrastructure over `IHubContext<TrackingHub>` — so the consumers/tests depend on an interface, not on SignalR types.
- Each consumer also upserts the **routing map** in Redis: `rt:order:{orderId}` → `{ customerId, restaurantId }` with a generous TTL (e.g. a few hours — an order's active window). Milestone C reads this to resolve a location frame's customer; storing it here means the map is warm before a driver is ever assigned.

### 3.2 Status mapping
A pure function `OrderStatusFrame.From(event)` maps each integration event to `{ orderId, status, occurredOnUtc }`. Keep the client-facing `status` string stable and decoupled from internal enums.

**Tests**
- *Unit*: the event→frame mapping for each of the five events (correct `status`, `orderId`, timestamp); the target group is `user:{customerId}` taken from the event's `CustomerId`.
- *Integration* (real RabbitMQ + Redis containers): connect as customer A and customer B; publish `OrderAcceptedIntegrationEvent{ CustomerId = A }` on the bus → **A** receives `OrderStatusChanged`, **B** receives nothing (group isolation); assert the `rt:order:{orderId}` map row was written. Publish the full sequence (placed→accepted→ready→cancelled) → A receives them in order.

---

## 4. Milestone C — Live driver location push to the tracking customer

The moving pin. Two small pieces: Delivery emits positions on Redis pub/sub; Real-Time binds driver→customer and forwards.

### 4.1 Delivery — publish location on Redis pub/sub (additive)
In `RecordDriverLocationCommandHandler` (or the `IDriverLocationStore.RecordAsync` implementation), after the existing GEO/history write, `PUBLISH delivery:driver-locations {driverId, latitude, longitude, recordedOnUtc}` (fire-and-forget, failures swallowed + logged — a lost position frame is immaterial). This is the **only** change to Delivery, and it touches neither the aggregate nor the bus. Guard it behind the same `ISubscriber`/`IConnectionMultiplexer` Delivery already has.

> This makes the Milestone-C PR span two services. It's a single ~10-line addition on the Delivery side with its own test, isolated from the Real-Time logic — acceptable, and cleaner than routing a hot-path stream through RabbitMQ.

### 4.2 Real-Time — driver→order→customer binding
- Consume `DriverAssignedIntegrationEvent` → look up `rt:order:{orderId}` (written in Milestone B) for the `customerId`, then write `rt:driver:{driverId}` → `{ orderId, customerId }` (TTL ~ a delivery window). Also fan out a `DriverAssigned` status frame (driver name is on the event) to `user:{customerId}`.
- Consume `OrderPickedUp` / `OrderDelivered` (Delivery) → fan out the corresponding status frames (this is how the timeline reaches "out for delivery" and "delivered" without new Orders events); on `OrderDelivered`/`OrderCancelled`, **delete** `rt:driver:{driverId}` so later stray positions are dropped.
- Also fan out `DeliveryOffered`, `DeliveryOfferRejected`, `DeliveryUnassigned` as status frames where they're useful to the customer/support (keep customer-facing set minimal; `DeliveryUnassigned` is mainly support).

### 4.3 Real-Time — the Redis location subscriber
A hosted `BackgroundService` subscribes to `delivery:driver-locations`. On each message: resolve `rt:driver:{driverId}` → if a binding exists, broadcast `DriverLocationChanged{ orderId, driverId, lat, lon, recordedOnUtc }` to `user:{customerId}`; if no binding (unassigned/finished driver), drop it. Wrap the resolve+forward in a named OTel activity.

**Tests**
- *Unit*: binding lifecycle over a fake store — `DriverAssigned` sets `rt:driver:{id}`, `OrderDelivered`/`OrderCancelled` clears it; the location→group resolver returns the customer group when bound and **nothing** when unbound (post-delivery drop).
- *Integration* (real Redis + RabbitMQ): publish `OrderReadyForPickup`/`OrderAccepted` (seeds `rt:order` map) then `DriverAssignedIntegrationEvent`; connect as the customer; `PUBLISH` a location on `delivery:driver-locations` → the customer receives `DriverLocationChanged`; publish `OrderDeliveredIntegrationEvent`, then publish another location → it is **not** delivered. (Optionally drive the real Delivery endpoint in-process to prove the PUBLISH fires, mirroring the cross-service pattern the Delivery suite already uses.)

---

## 5. Milestone D — Restaurant dashboard + Support dashboard channels

Extends fan-out to the other two audiences. This is where the service gains a small Postgres replica, because a restaurant manager's `restaurantId` is **not** in their JWT and can't be fetched synchronously across services.

### 5.1 Minimal `RestaurantManager` replica (the service's first DB)
- Add `DbContext` + `AddDbContext` to the host (now the `Database` env/`fooddeliveryservice_realtime` DB is introduced), `InsertOutboxMessagesInterceptor` **not needed** (no outbox — this service publishes nothing). Just the replica + inbox is likewise unnecessary since consumers are direct. So: DbContext with a single read model, migrations auto-applied.
- `RestaurantManager` read model: `Id (= managerUserId)`, `RestaurantId`, `RestaurantName`, upserted from `RestaurantRegisteredIntegrationEvent` (+ `RestaurantAddressUpdatedIntegrationEvent`/name change if present). Same upsert-from-event pattern as every other replica; these consumers **can** be direct too, but if you want the manager mapping to survive a cold start reliably, register these two as the **inbox** variant so a missed event is retried — a reasonable, localized exception to the "all direct" rule, justified because this mapping must be durable (unlike a transient frame). Call the choice out in the PR.

### 5.2 Dashboard group membership + fan-out
- `OnConnectedAsync` (extended): if the caller's role is `RestaurantManager`, resolve `RestaurantId` from the replica and join `restaurant:{restaurantId}`; if `SupportAgent`, join `support`. A manager with no replica row yet joins nothing (logs a warning) — self-heals when the event arrives and the client reconnects.
- The Orders/Delivery consumers from Milestones B/C additionally fan out `RestaurantActivity` to `restaurant:{restaurantId}` (new order arrived, status changed) and `SupportActivity` to `support` (a coarse live feed of all order/delivery transitions).

**Tests**
- *Unit*: role→group resolution (manager with a mapped restaurant joins `restaurant:{id}`; manager without a mapping joins nothing; support joins `support`; customer joins only `user:*` — never a restaurant/support group).
- *Integration*: seed the replica by publishing `RestaurantRegisteredIntegrationEvent`; connect as that restaurant's manager → they receive a `RestaurantActivity` frame for an order at **their** restaurant and **not** for another restaurant's order; a support agent receives `SupportActivity`; a **customer** token connecting cannot receive `restaurant:*`/`support` frames (never joined those groups).

---

## 6. Milestone E *(optional, portfolio)* — Azure SignalR Service backplane

Nothing above depends on this; it is the managed-Azure showcase, deferred behind config.

Swap the Redis backplane for **Azure SignalR Service** via `AddSignalR().AddAzureSignalR(connectionString)`, selected by configuration (`RealTime:Backplane: "Redis" | "AzureSignalR"`). In Azure SignalR's *Default* mode the service also offloads the WebSocket connections themselves. Document the scale-out story (multiple stateless Real-Time instances, connections owned by the managed service). Keep Redis as the default so the suite stays green offline; any Azure-mode test is a separately-marked, non-default collection.

---

## 7. Cross-cutting checklist

- **Best-effort, re-sync on connect:** the client's contract is "GET the read models on (re)connect, then apply socket deltas." Document this in the hub's XML doc and the PR — it's the load-bearing assumption behind skipping the inbox.
- **Auth & group integrity:** connections authenticated by Duende JWT (validated at gateway *and* hub); `access_token` query hook scoped to `/hubs/*`; groups derived from claims only, never from client input. A customer can never subscribe to another user's, a restaurant's, or the support group.
- **Observability:** the host inherits `AddInfrastructure` (OTel + Serilog + Seq). SignalR hub method calls and the Redis-subscriber forward are **not** auto-instrumented — wrap the fan-out path and the location-forward in a named `ActivitySource` so a stuck dashboard is debuggable; enrich the log context with `OrderId`/`DriverId`. Add a health check for Redis (and Postgres from Milestone D).
- **Departure from the inbox rule:** status consumers are direct `IConsumer<T>` (ephemeral fan-out); only the Milestone-D `RestaurantManager` replica consumers optionally use the inbox (durable mapping). This is the one service that intentionally diverges from presentation-messaging rules — keep the justification in code comments so a reviewer doesn't "fix" it.
- **Hard rules that still apply:** own DB only (the replica; no querying another service's tables); consume only other modules' `*.IntegrationEvents`; reads via Dapper if any REST endpoint is added (none planned — the hub is the surface); saves via `IUnitOfWork.SaveChangesAsync()` for the replica.
- **Gateway:** one new `hubs/**` route + cluster, authenticated, WebSocket forwarded; no anonymous route.
- **Rate limiting:** driver location is already the hottest endpoint (Delivery). The Real-Time *socket* is long-lived (one per client) so it's cheap; the load is the location *fan-out*, bounded by concurrent tracked orders. Note the gateway rate-limit bucket for `hubs/negotiate` should allow reconnect storms; don't solve it here.
- **Migration analyzer gotcha (Milestone D only):** after `dotnet ef migrations add`, convert to file-scoped namespace + `[SuppressMessage]` for `CA1861`/`IDE0300` where arrays seed (as in the other modules).

---

## 8. Milestones (each buildable, verifiable, review-sized)

| # | Milestone | Size | Touches |
|---|---|---|---|
| **A** | Real-Time service skeleton + authenticated `TrackingHub` + connection mgmt + gateway `hubs/**` | medium | new host/module, gateway, docker-compose |
| **B** | Order status push to the customer (`user:{customerId}`) + Redis routing map | small–medium | RealTime only |
| **C** | Live driver location push (Delivery `PUBLISH` + RealTime subscriber + driver→customer binding) | medium | RealTime + 1-line Delivery |
| **D** | Restaurant dashboard + Support dashboard channels + `RestaurantManager` replica (first DB) | medium | RealTime only |
| **E** | *(optional)* Azure SignalR Service backplane | small | RealTime config |

Each milestone is independently buildable, has its own unit + integration tests, and is a self-contained PR. A–B deliver the headline customer experience; C adds the map; D adds the operator dashboards.

---

## 9. Definition of done

- `dotnet build` clean; `docker-compose up -d` brings up `fooddeliveryservice.realtime.api`; the gateway routes `hubs/**` to it and forwards WebSocket upgrades.
- A customer opens an authenticated socket and, without refreshing, sees each order-status transition (`placed → accepted → ready → out-for-delivery → delivered`, and `rejected`/`cancelled`) as it happens; an unauthenticated or wrong-token handshake is rejected.
- Once a driver is assigned, the customer's map pin moves as the driver reports location — driven over Redis pub/sub, with **no** location traffic on RabbitMQ; after delivery/cancellation, stray positions stop flowing.
- A restaurant manager's dashboard updates live for **their** restaurant's orders only; a support agent sees a live global activity feed; neither a customer nor a manager can receive another audience's frames.
- Reconnects re-establish the socket and the client re-syncs from the REST read models — no missed-update corruption; a dropped frame is never a bug.
- Unit tests cover group-name derivation, claim→group membership, the event→frame mappings, and the driver-binding lifecycle. Integration tests cover authenticated connect/handshake-reject, per-group isolation, status fan-out, location forward-and-drop, and dashboard scoping — all against real Duende JWTs + RabbitMQ + Redis (+ Postgres for D) containers.
- No hard-rule violations beyond the single, documented inbox departure for ephemeral status frames.

---

## 10. Deferred / open (not this iteration)

- **Azure SignalR Service** — optional Milestone E; Redis backplane is the default.
- **Mobile background push** — SignalR only reaches a foreground app; true background push is Notifications' `PushNotificationChannel` over Azure Notification Hubs → FCM/APNs (`NOTIFICATIONS_PHASE1_PLAN.md` §7). Separate line of work.
- **Live ETA on the tracking screen** — Feature 3.3 pushes its updating ETA over this same hub (`OrderStatusFrame` gains an `etaMinutes` field additively).
- **Presence / "driver is typing"-style features, chat** — out of scope; the hub is one-way (server→client) fan-out this iteration.
- **Retiring the Notifications `Realtime` channel enum** — superseded by this service; a mechanical cleanup, not bundled here (§11).
- **Backfilling the `RestaurantManager` replica** for restaurants onboarded before this service existed — a dev re-onboard or a one-off replay; no production data.
- **Per-frame delivery guarantees / replay** — deliberately not built; the REST re-sync is the recovery mechanism.

---

## 11. Reconciliation with the Notifications plan

`NOTIFICATIONS_PHASE1_PLAN.md` §7 reserved a `SignalRNotificationChannel` behind `INotificationChannel`, anticipating that Notifications would host the socket. This plan **moves that responsibility to a dedicated Real-Time service** for the separation-of-concerns reasons in §Decisions. Consequences:
- The Notifications `NotificationChannel.Realtime` enum value and its reserved channel slot are **superseded** — Notifications keeps owning *durable* channels (Email now, Push later), and does **not** grow a SignalR channel. Leave the enum value in place (harmless) and note it as dead in a follow-up cleanup, or drop it in the Milestone-D PR if convenient.
- The two "restaurant new-order alert" and "status-change" audiences that §7 imagined delivering via a Notifications SignalR channel are delivered here instead (Milestones B–D), consuming the same events. Notifications is unchanged by this plan.
