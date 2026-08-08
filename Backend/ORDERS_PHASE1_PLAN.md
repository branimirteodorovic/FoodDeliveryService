# Feature 1.5 — Order Service — Implementation Plan

> **Naming note:** this is the *second* module implementation plan (after `RESTAURANTS_PHASE1_PLAN.md`). It covers **Feature 1.5 — Order Service**, which belongs to **Phase 1** of `FoodDelivery_ProjectPlan.md`. "Phase 2" in the request refers to this being the next planning iteration, not the project's Phase 2 (real-time/observability).

> Scope for this iteration: a **Customer** can place an order against a restaurant's current menu; the order runs through a strict **status state machine**; a **Restaurant Manager** can accept/reject/advance it; a customer can cancel while still allowed. Placement is **idempotent**. Every status change publishes an integration event via the outbox. Driver/delivery transitions (`OutForDelivery`, `Delivered`) are modeled in the domain but **driven later by the Delivery service (Phase 2)** — no delivery work here. Payment is **cash-on-delivery only**; promotions are **out of scope** (stubbed).

Decisions locked in for this plan:
- **Orders owns order state; it never queries another service's database.** It keeps **local replicas** of the data it needs to place and price an order: a `Customer` replica (from `UserRegisteredIntegrationEvent`/`UserProfileUpdatedIntegrationEvent`) and a `Restaurant` + `MenuItem` replica (from Restaurants integration events). This mirrors the existing pattern (Orders already keeps a local copy of users) and keeps placement resilient when Restaurants/Users are down — no synchronous cross-service call on the hot path.
- **Prices are authoritative from the server, never the client.** Placement recomputes every line total from the local `MenuItem` replica and rejects unknown/unavailable items. The client sends item ids + quantities only.
- **Idempotent placement via a client `Idempotency-Key`.** A unique constraint in the Orders DB guarantees a repeated key returns the *same* order instead of creating a second one.
- **The state machine lives in the `Order` aggregate.** Handlers only orchestrate; every transition is a guarded domain method that raises a domain event. Illegal transitions return a `Result.Failure`, never throw.
- **One new Users-side change only:** an `orders:manage` permission for the manager-facing transitions (see §2). Everything else Orders consumes from Users already exists.
- Reference implementations to mirror: the **Users** module (aggregate, RPC, events) and the **Restaurants** module (replica upsert handlers, ownership check, menu domain).

---

## 1. Architecture overview

Modules touched this iteration:

| Module | Responsibility this iteration |
|---|---|
| **Orders** (`fooddeliveryservice_orders`) | Owns `Order` (+ `OrderItem`). Keeps `Customer`, `Restaurant`, `MenuItem` replicas. Placement, state machine, transition/cancel endpoints, read queries. Publishes order lifecycle integration events. |
| **Restaurants** (`fooddeliveryservice_restaurants`) | Adds **menu integration events** (`MenuItemAdded/Updated/AvailabilityChanged`) so Orders can maintain its menu replica. `RestaurantRegisteredIntegrationEvent` already exists and already carries `ManagerUserId` + `CommissionRate`. |
| **Users** (`fooddeliveryservice_users`) | Adds the `orders:manage` permission and assigns it to `RestaurantManager` + `Administrator`. No other change — the customer replica is fed by the existing `UserRegistered`/`UserProfileUpdated` events, and authorization already resolves via `GetUserPermissionsRequest`. |
| **Notifications** | **No work here.** Order lifecycle events are published now; Notifications consumes them in **Feature 1.6**. |

Cross-service contact stays on the bus — integration events only; **no new synchronous RPC** is introduced.

**End-to-end flow (place → accept → prepare → ready)**

1. A **Customer** (authenticated) → `POST orders` with `RestaurantId`, `[{ MenuItemId, Quantity }]`, delivery address, `PaymentMethod=CashOnDelivery`, and an `Idempotency-Key` header.
2. `PlaceOrderCommand` handler: looks up the `Customer` replica, the `Restaurant` replica, and the referenced `MenuItem` replicas (all local). Rejects unknown restaurant, unknown/unavailable items. Recomputes each line from the replica price and the order total. If the idempotency key was already used, returns the existing order id.
3. `Order.Place(...)` creates the aggregate in `Pending`, raises `OrderPlacedDomainEvent`; the handler saves it in one unit of work. The outbox publishes `OrderPlacedIntegrationEvent`.
4. The **Restaurant Manager** (owner of `order.RestaurantId`) → `POST orders/{id}/accept` (or `/reject`, `/preparing`, `/ready`). Each is a guarded transition raising a domain event → integration event.
5. The **Customer** may `POST orders/{id}/cancel` while the order is still cancellable (Pending/Accepted, per the domain rules).
6. Each transition publishes its integration event via the outbox; **Feature 1.6** will wire Notifications consumers to them.

---

## 2. Users module change (minimal)

### 2.1 Permission (`Domain/Users/Permission.cs`)
Add one manager-facing order permission (customer order permissions `orders:create`/`orders:read` and the `carts:*` set already exist and are already assigned to `Customer`):
```csharp
public static readonly Permission ManageOrders = new("orders:manage"); // accept/reject/advance order status
```

### 2.2 Role → permission seeding (`Infrastructure/Users/PermissionConfiguration.cs`)
- Add `Permission.ManageOrders` to `HasData(...)`.
- Assign `orders:manage` to **`Administrator`** (oversight) and **`RestaurantManager`** (their own restaurants — ownership enforced in the Orders handler).
- **Do not** grant it to `Customer`.
- Migration: `Add_Order_Management_Permission` (new `permissions` row + two `role_permissions` rows).

> That is the entire Users footprint. `orders:create`/`orders:read` on `Customer` cover placement and self-service reads already.

---

## 3. Orders module — replicas (Milestones A & B)

All replicas are minimal read models, keyed by the owning service's id, upserted from integration events (same pattern as Restaurants' `RestaurantManager`). They live under the Orders module's own `DbContext`/database.

### 3.1 `Customer` replica (Milestone A)
```
Id (= UserId)  Guid
Email          string
FirstName      string
LastName       string
```
Fed by `UserRegisteredIntegrationEvent` (upsert) and `UserProfileUpdatedIntegrationEvent` (name sync). The consumers are **already registered** in `OrdersModule.ConfigureConsumers`; only the handlers + entity are missing. Mirror `RestaurantManager` + `UpsertRestaurantManagerCommandHandler`.

### 3.2 `Restaurant` replica (Milestone B)
```
Id (= RestaurantId)  Guid
ManagerUserId        Guid     // for ownership checks on transitions
Name                 string   // display / denormalization onto the order
CommissionRate       decimal  // snapshot for later commission splits
```
Fed by `RestaurantRegisteredIntegrationEvent` (already published, already carries `ManagerUserId` + `CommissionRate`).

### 3.3 `MenuItem` replica (Milestone B)
```
Id (= MenuItemId)  Guid
RestaurantId       Guid
Name               string    // snapshot onto order lines for display/audit
Price              decimal   // authoritative price at placement time
IsAvailable        bool
```
Fed by the new Restaurants menu integration events (§5.2). This is the source of truth for placement pricing and availability.

### 3.4 Persistence
- Add `DbSet<Customer>`, `DbSet<Restaurant>`, `DbSet<MenuItem>` (replicas) to `OrdersDbContext`, one `IEntityTypeConfiguration` each (snake_case).
- Migrations: `Add_Orders_Customer_Replica` (Milestone A), `Add_Orders_Menu_Replicas` (Milestone B).
- Repositories: `ICustomerRepository`, `IRestaurantReplicaRepository`, `IMenuItemReplicaRepository` (`GetAsync`, `Insert`; the menu one also `GetManyAsync(restaurantId, ids)` for placement).

---

## 4. Orders module — the `Order` aggregate (Milestone C)

Replaces the current stub (`Order.Create(Guid id)`). All business rules live here; handlers only orchestrate. Mirror `Users/User.cs` and `Restaurants/Restaurant.cs`.

### 4.1 `Order` (aggregate root)
```
Id               Guid
CustomerId       Guid
RestaurantId     Guid
Status           OrderStatus
DeliveryAddress  DeliveryAddress   // owned value object
PaymentMethod    PaymentMethod     // CashOnDelivery only this iteration
Subtotal         decimal           // Σ line totals (server-computed)
CommissionRate   decimal           // snapshot from the Restaurant replica
IdempotencyKey   string            // unique; guards duplicate placement
PlacedOnUtc      DateTime
_items           List<OrderItem>   // private; exposed read-only
```
Factory `Order.Place(customerId, restaurantId, address, paymentMethod, IReadOnlyCollection<OrderLine> lines, commissionRate, idempotencyKey, utcNow)` → validates there is ≥1 line, builds `OrderItem`s, sets `Status = Pending`, computes `Subtotal`, raises `OrderPlacedDomainEvent`. Returns `Result<Order>` (`OrderErrors.Empty` if no lines).

### 4.2 `OrderItem` (child)
```
Id           Guid
OrderId      Guid
MenuItemId   Guid
Name         string    // snapshot at placement (menu may change later)
UnitPrice    decimal   // snapshot from replica — never client-supplied
Quantity     int
LineTotal    decimal   // UnitPrice * Quantity
```

### 4.3 State machine — `OrderStatus` + guarded transitions
`enum OrderStatus { Pending, Accepted, Rejected, Preparing, ReadyForPickup, OutForDelivery, Delivered, Cancelled }`

Domain methods (each: guard → mutate → raise event → `Result`):
| Method | Allowed from | → | Raises | Actor (this iteration) |
|---|---|---|---|---|
| `Accept(utcNow)` | Pending | Accepted | `OrderAcceptedDomainEvent` | Restaurant |
| `Reject(reason, utcNow)` | Pending | Rejected | `OrderRejectedDomainEvent` | Restaurant |
| `StartPreparing()` | Accepted | Preparing | `OrderPreparingDomainEvent` | Restaurant |
| `MarkReadyForPickup()` | Preparing | ReadyForPickup | `OrderReadyForPickupDomainEvent` | Restaurant |
| `Cancel(utcNow)` | Pending, Accepted | Cancelled | `OrderCancelledDomainEvent` | Customer |
| `MarkOutForDelivery()` | ReadyForPickup | OutForDelivery | `OrderOutForDeliveryDomainEvent` | **Delivery svc — modeled, not exposed** |
| `MarkDelivered(utcNow)` | OutForDelivery | Delivered | `OrderDeliveredDomainEvent` | **Delivery svc — modeled, not exposed** |

Any disallowed transition → `Result.Failure(OrderErrors.InvalidTransition(from, to))`. `OutForDelivery`/`Delivered` methods exist so the machine is complete and unit-testable, but **no endpoints** expose them this iteration.

### 4.4 Value objects, errors, events
- `DeliveryAddress(Street, City, PostalCode, Country, Notes?)` — `OwnsOne`.
- `PaymentMethod` enum — `CashOnDelivery` only.
- `OrderErrors` — `NotFound(id)`, `Empty`, `RestaurantNotFound`, `MenuItemNotFound(id)`, `MenuItemUnavailable(id)`, `InvalidTransition(from, to)`, `NotOwner`, `DuplicateIdempotencyKey`.
- Domain events as listed in §4.3, plus `OrderPlacedDomainEvent`.

---

## 5. Cross-service messaging

### 5.1 Async events **consumed** by Orders
Registered in `OrdersModule.ConfigureConsumers` (`.Endpoint(c => c.InstanceId = instanceId)`), each with an `IIntegrationEventHandler<T>` in `Orders.Presentation`:
- `UserRegisteredIntegrationEvent`, `UserProfileUpdatedIntegrationEvent` → upsert `Customer` replica *(consumers already registered; add handlers — Milestone A)*.
- `RestaurantRegisteredIntegrationEvent` → upsert `Restaurant` replica *(Milestone B)*.
- `MenuItemAddedIntegrationEvent`, `MenuItemUpdatedIntegrationEvent`, `MenuItemAvailabilityChangedIntegrationEvent` → upsert `MenuItem` replica *(Milestone B)*.

### 5.2 New Restaurants menu integration events (Milestone B, publish side)
The menu **domain events already exist** (`MenuItemAddedDomainEvent`, `MenuItemUpdatedDomainEvent`, `MenuItemPriceChangedDomainEvent`, `MenuItemAvailabilityChangedDomainEvent`). Add in `Restaurants.IntegrationEvents` full-snapshot contracts and, in `Restaurants.Application`, domain-event handlers that publish them via `IEventBus` (mirror `RestaurantRegisteredDomainEventHandler`; fetch the snapshot with a small Dapper query):
- `MenuItemAddedIntegrationEvent(restaurantId, menuItemId, name, price, isAvailable)`
- `MenuItemUpdatedIntegrationEvent(restaurantId, menuItemId, name, price, isAvailable)` — covers both detail and price changes (collapse `MenuItemUpdated*`/`MenuItemPriceChanged*` handlers onto this one snapshot so the replica stays whole)
- `MenuItemAvailabilityChangedIntegrationEvent(restaurantId, menuItemId, isAvailable)`

> Snapshots carry everything Orders needs (hard rule #9) so the replica never calls back.

### 5.3 Order lifecycle events **published** by Orders (Milestones C & D)
In `Orders.IntegrationEvents`, publish via the outbox from `Application` domain-event handlers (mirror `UserRegisteredDomainEventHandler`):
- `OrderPlacedIntegrationEvent` (orderId, customerId, restaurantId, subtotal, placedOnUtc) — Milestone C
- `OrderAcceptedIntegrationEvent`, `OrderRejectedIntegrationEvent(reason)`, `OrderReadyForPickupIntegrationEvent`, `OrderCancelledIntegrationEvent` — Milestone D

Consumers (Notifications, later Delivery) are **not** added here — that is Feature 1.6 / Phase 2.

---

## 6. Endpoints

Commands = EF + `IUnitOfWork.SaveChangesAsync()`; queries = Dapper via `IDbConnectionFactory` (hard rule #2). Every endpoint is an `IEndpoint`, returns `result.Match(Results.Ok, ApiResults.Problem)`, `.WithTags("Orders")`, and `.RequireAuthorization(<permission>)`. All fall under the existing `orders/{**catch-all}` YARP route — **no gateway change needed**.

| Method & path | Auth | Command/Query | Purpose |
|---|---|---|---|
| `POST orders` | `orders:create` (Customer) | `PlaceOrderCommand` | Place an order; server-priced; idempotent via `Idempotency-Key`. |
| `GET orders/{id}` | `orders:read` | `GetOrderQuery` (Dapper) | Order detail DTO (owner or owning manager/admin). |
| `GET orders` | `orders:read` | `GetOrdersQuery` (Dapper, paged) | Caller's orders — customer's own, or a manager's incoming (by `RestaurantId`). |
| `POST orders/{id}/accept` | `orders:manage` | `AcceptOrderCommand` | Restaurant accepts. Ownership-checked. |
| `POST orders/{id}/reject` | `orders:manage` | `RejectOrderCommand` | Restaurant rejects (reason). Ownership-checked. |
| `POST orders/{id}/preparing` | `orders:manage` | `StartPreparingOrderCommand` | Advance to Preparing. Ownership-checked. |
| `POST orders/{id}/ready` | `orders:manage` | `MarkOrderReadyCommand` | Advance to ReadyForPickup. Ownership-checked. |
| `POST orders/{id}/cancel` | `orders:create` (Customer) | `CancelOrderCommand` | Customer cancels while allowed. Owner-checked. |

**Ownership enforcement** (mirror `RestaurantOwnership`): transition handlers load the order + the `Restaurant` replica and require `restaurant.ManagerUserId == IOrdersContext.UserId`, with an admin bypass (admin holds an admin-only permission that managers do not). Customer cancel requires `order.CustomerId == IOrdersContext.UserId`. Mismatch → `OrderErrors.NotOwner` (403).

---

## 7. Idempotency (placement)

- The client sends an `Idempotency-Key` header; the endpoint maps it into `PlaceOrderCommand`.
- `orders` table gets a **unique index on `idempotency_key`** (scoped per customer is fine; global is simpler and acceptable given GUID/opaque keys).
- Handler: look up an existing order by key first → if found, return its id (treat as success, `200`). Otherwise place. Guard the race by catching the unique-constraint violation on save and re-reading the existing order, so two concurrent identical requests still yield one order.
- Validator requires a non-empty key.

---

## 8. Cross-cutting checklist

- **Validation (FluentValidation):** `RestaurantId`/`MenuItemId` non-empty; `Quantity > 0`; ≥1 line; address fields non-empty; `PaymentMethod` in enum; `Idempotency-Key` present. (Business checks — unknown/unavailable item, illegal transition — stay in the domain/handler, not the validator.)
- **Observability:** Orders host already inherits `AddInfrastructure` (OTel + Serilog + EF/Npgsql/MassTransit instrumentation). No new external calls are added, so nothing extra to instrument.
- **No gateway change:** all routes are under `orders/**`; all authenticated.
- **Migrations** auto-apply via `app.ApplyMigrations()`.
- **Hard rules:** reads Dapper-only; writes via `IUnitOfWork.SaveChangesAsync()`; no cross-module project references beyond `*.IntegrationEvents`; integration events are full snapshots; Orders queries only its own DB (hence the replicas).

---

## 9. Milestones (each buildable, verifiable, review-sized)

### Milestone A — Users permission + Customer replica *(small)*
1. Users: add `orders:manage` (§2) + assign to Administrator/RestaurantManager; migration.
2. Orders: `Customer` replica entity + repo + EF config + migration.
3. Orders: `UpsertCustomerCommand` + `UserRegisteredIntegrationEventHandler` + `UserProfileUpdatedIntegrationEventHandler` in `Orders.Presentation` (consumers already registered).
- **Verify:** `dotnet build` clean; migrations apply; register a customer → a `customers` row appears in the Orders DB; a profile update syncs the name; `GetUserPermissionsRequest` returns `orders:manage` for a manager/admin.

### Milestone B — Menu & Restaurant replicas *(medium)*
4. Restaurants: add the 3 menu integration-event contracts (§5.2) + `Application` domain-event handlers publishing them via the outbox.
5. Orders: `Restaurant` + `MenuItem` replica entities + repos + configs + migration.
6. Orders: consumers + handlers for `RestaurantRegistered` + the 3 menu events; register the new consumers in `OrdersModule.ConfigureConsumers`.
- **Verify:** onboard a restaurant → `restaurants` replica row in Orders DB; add/edit a menu item + toggle availability → `menu_items` replica reflects current price/availability. *(If review size is a concern, split into B1 = Restaurants publish, B2 = Orders consume.)*

### Milestone C — Order aggregate + placement + idempotency *(core)*
7. Orders domain: `Order` + `OrderItem` + `OrderStatus` + `DeliveryAddress` + `PaymentMethod` + `OrderErrors` + `OrderPlacedDomainEvent`; the full state-machine methods from §4.3 (unit-testable).
8. Orders application: `PlaceOrderCommand` + validator + handler (replica lookups, server-side pricing, availability checks, idempotency); `OrderPlacedDomainEventHandler` → `OrderPlacedIntegrationEvent`.
9. Orders presentation: `POST orders` (+ `Idempotency-Key`). Migration `Add_Orders` (unique index on `idempotency_key`).
- **Verify:** place an order → persisted `Pending` with server-computed totals and snapshotted line prices; the same `Idempotency-Key` twice → **one** order; unknown/unavailable item or unknown restaurant → clean `Problem`, not 500; `OrderPlacedIntegrationEvent` lands in the outbox.

### Milestone D — Transitions, cancellation, queries, events *(completes feature)*
10. Orders presentation/application: `accept`/`reject`/`preparing`/`ready` (`orders:manage`, ownership via `Restaurant` replica, admin bypass) and customer `cancel` (`orders:create`, owner).
11. Queries (Dapper): `GetOrderQuery`, `GetOrdersQuery` (customer's own / manager's incoming) → DTOs.
12. Publish lifecycle integration events (Accepted/Rejected/ReadyForPickup/Cancelled).
- **Verify:** full happy path Pending→Accepted→Preparing→ReadyForPickup; illegal transitions (e.g. Ready before Accepted, accept someone else's restaurant's order) rejected (`InvalidTransition`/`NotOwner`); customer cancel works while allowed and is blocked afterwards; reads via Dapper only; each transition emits its integration event.

---

## 10. Definition of done

- `dotnet build` clean; all new migrations apply on startup.
- A `Customer` token places an order that is **server-priced** from the menu replica; client-supplied prices are ignored; duplicate `Idempotency-Key` yields one order.
- A `RestaurantManager` can accept/reject/advance **only their own** restaurant's orders (`orders:manage` + ownership); a `Customer` is `403` on those; a second manager cannot touch the first's orders (`NotOwner`); an Administrator can.
- The state machine enforces legal transitions only; illegal ones return a clean failure, never throw.
- Every status change publishes its integration event via the outbox (verifiable in RabbitMQ / Seq); Notifications wiring is deferred to Feature 1.6.
- Replicas stay current: customer name syncs on profile update; menu price/availability changes propagate to the Orders replica.
- No hard-rule violations: reads Dapper; cross-service contact only via the bus; domain logic in the entity; Orders touches only its own DB.

---

## 11. Open questions / deferred (not this iteration)

- **Promotions/discounts** — the plan mentions them; no promotion subsystem exists → deferred (subtotal only for now).
- **Delivery transitions** (`OutForDelivery`, `Delivered`) — modeled in the domain but driven by the **Delivery service** in Phase 2 (Feature 2.1); no endpoints here.
- **Commission split** — `CommissionRate` is snapshotted onto the order for later payout math; no settlement/split logic this iteration.
- **Notifications on order events** — Feature 1.6 (consumers + templates + email).
- **Menu-item deletion** — no delete flow exists in Restaurants Phase 1; add a `MenuItemRemovedIntegrationEvent` when it does.
- **Cart** — `carts:*` permissions exist but a server-side cart is out of scope; placement takes the line items directly.
```
