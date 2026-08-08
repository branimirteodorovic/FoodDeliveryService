# Feature 2.1 — Delivery Service & Driver Management — Implementation Plan

> Fourth module implementation plan, after `RESTAURANTS_PHASE1_PLAN.md`, `ORDERS_PHASE1_PLAN.md` and `NOTIFICATIONS_PHASE1_PLAN.md`. This one covers **Feature 2.1 — Delivery Service & Driver Management**, the first feature of **Phase 2** in `FoodDelivery_ProjectPlan.md`.

> Scope for this iteration: a **new Delivery service** owning driver profiles and the delivery leg of every order. An **Administrator** onboards a driver (invitation flow, same as restaurant managers). A driver goes **available**, streams their **location**, is **offered** the nearest ready order, **accepts or rejects** it, and marks it **picked up** then **delivered** — which drives the `Order` aggregate's last two transitions from the Delivery side. Real-time push to the customer (SignalR), Redis caching of menus, and Redlock are **Features 2.2/2.3** — not here.

Decisions locked in for this plan:

- **Delivery is a new service**, `fooddeliveryservice.delivery.api` on `:5500`, database `fooddeliveryservice_delivery`, routed at `delivery/**`. It owns `Driver` and `Delivery`; it keeps local replicas of the restaurant and order data it needs, exactly as Orders does.
- **Live driver positions live in Redis GEO; history goes to Postgres — both behind `IDriverLocationStore`.** `GEOSEARCH` answers "nearest available driver" natively in sub-millisecond time, and Redis + Postgres are already in `docker-compose` and available as Testcontainers, so the geospatial path is **fully covered by integration tests**. A **Cosmos DB implementation of the same interface is Milestone G (optional)** — it swaps in behind the interface with no domain change, and exists for the Azure/NoSQL portfolio story. This is a deliberate departure from the project plan's "store location history in Cosmos DB", which has no clean local/test story.
- **Assignment state lives in a `Delivery` aggregate; a Quartz job handles offer timeouts.** The offer → accept/reject → **timeout → next-nearest driver** loop is modeled directly on the `Delivery` aggregate in the Delivery DB — including which drivers have already been tried and when the current offer expires. A periodic `ProcessExpiredOffersJob` (Quartz — already in the stack for outbox/inbox) scans for offers past their deadline and re-offers to the next candidate. This keeps the whole delivery record **Dapper-queryable**, which Feature 2.2 needs to render the tracking screen, and the offer rules become plain domain unit tests. No saga, no message-scheduler infrastructure, no Redis saga repository. Because the offer deadline is a column in Postgres, timeouts are **inherently durable** — they survive a service restart regardless of Quartz's job store, since the job re-derives what's expired from the database on each run.
- **Drivers are admin-provisioned, never self-registered** — same invitation/activation flow the restaurant managers use (`RESTAURANTS_PHASE1_PLAN.md` §5.3). The provisioning RPC is generalized to carry a role rather than adding a per-role sibling contract.
- **Delivery drives Orders only through the bus.** Delivery publishes `OrderPickedUp`/`OrderDelivered`; Orders consumes them and calls the `MarkOutForDelivery()`/`MarkDelivered()` methods that already exist on the `Order` aggregate but have no caller yet.
- Reference implementations to mirror: **Restaurants** for admin onboarding + the provisioning RPC, **Orders** for replicas + a status-driven aggregate, **Notifications** for a consumer-shaped module.

---

## 0. Blocking prerequisite — Orders Phase 1 Milestone D

**Feature 2.1 cannot start until `ORDERS_PHASE1_PLAN.md` Milestone D ships.** This is a hard gate, tracked under that plan, not this one.

The Delivery service is triggered by `OrderReadyForPickupIntegrationEvent`. Today that event **does not exist**: `Orders.IntegrationEvents` contains only `OrderPlacedIntegrationEvent`, `Orders.Application` contains only the `PlaceOrder` slice, and `Orders.Presentation` exposes only `POST orders`. The `Order` aggregate's transition methods (`Accept`, `StartPreparing`, `MarkReadyForPickup`, …) and their domain events are all implemented and unit-tested — but nothing calls them and nothing publishes their integration events.

Concretely, Milestone D must land: the `accept`/`reject`/`preparing`/`ready` endpoints, and the domain-event handlers that publish `OrderAccepted`/`OrderRejected`/`OrderReadyForPickup`/`OrderCancelled` integration events via the outbox. Milestone D of this plan (§7) then extends the `OrderReadyForPickup` contract with the geo fields Delivery needs.

---

## 1. Architecture overview

| Module | Responsibility this iteration |
|---|---|
| **Delivery** (`fooddeliveryservice_delivery`) — **new** | Owns `Driver` (profile, vehicle, availability) and `Delivery` (the delivery leg of one order). Keeps `Restaurant` + `Order` replicas. Hosts the assignment routine + offer-expiry job and the location store. Publishes `DriverAssigned`, `OrderPickedUp`, `OrderDelivered`. |
| **Users** (`fooddeliveryservice_users`) | Adds the `DeliveryDriver` role, the delivery permission set, and a **generalized** `ProvisionUserRequest(…, Role)` RPC. Nothing else — invitation/activation already works. |
| **Orders** (`fooddeliveryservice_orders`) | Consumes `OrderPickedUpIntegrationEvent`/`OrderDeliveredIntegrationEvent` → calls the existing `MarkOutForDelivery()`/`MarkDelivered()`. Extends `OrderReadyForPickupIntegrationEvent` with restaurant + delivery-address coordinates. Adds coordinates to `DeliveryAddress`. |
| **Restaurants** (`fooddeliveryservice_restaurants`) | Adds `Latitude`/`Longitude` to `RestaurantRegisteredIntegrationEvent` (the `Address` value object already has the optional fields; the contract drops them) and publishes an address-change event so Delivery's replica stays current. |
| **Notifications** | **No work here.** Delivery's events are published now; the "driver assigned — here's their name" email is a Phase 2 follow-up. |

**End-to-end flow (ready → assigned → delivered)**

1. A **Restaurant Manager** marks an order ready (Orders Milestone D) → `OrderReadyForPickupIntegrationEvent` carrying the restaurant's coordinates and the delivery address.
2. **Delivery** consumes it and creates a `Delivery` aggregate in `Pending` (idempotent on `OrderId`), then runs the offer routine.
3. The offer routine asks `IDriverLocationStore` for the nearest **available** drivers to the restaurant (Redis `GEOSEARCH`), takes the best candidate not already tried, and offers the delivery to them → `Delivery.OfferTo(driverId, expiresAt)` → `DeliveryOfferedIntegrationEvent`. `expiresAt` = now + the offer window (30s), recorded on the aggregate.
4. The **driver** `POST delivery/deliveries/{id}/accept` (or `/reject`). Accept → `Delivery.AcceptOffer(...)` → the driver flips to `Busy` and leaves the geo set → `DriverAssignedIntegrationEvent`. Reject → the reject handler re-runs the offer routine for the next-nearest candidate. If nobody accepts before the deadline, `ProcessExpiredOffersJob` (Quartz) expires the offer and re-offers. Candidates exhausted → the delivery parks in `Unassigned` for admin/support (`DeliveryUnassignedIntegrationEvent`).
5. The driver streams location (`POST delivery/drivers/me/location`) throughout. Feature 2.2 turns this into a live map.
6. The driver marks `/picked-up` then `/delivered` → `OrderPickedUpIntegrationEvent`, `OrderDeliveredIntegrationEvent`. **Orders** consumes both and advances the `Order` through `OutForDelivery` → `Delivered`. The driver returns to `Available` and re-enters the geo set.
7. If the order is cancelled mid-flight, an `OrderCancelledIntegrationEvent` consumer cancels the delivery and releases the driver.

No synchronous cross-service call is added except the provisioning RPC, which mirrors the existing restaurant-manager one.

---

## 2. Users module changes (Milestone A)

### 2.1 Role (`Domain/Users/Role.cs`)
```csharp
public static readonly Role DeliveryDriver = new("DeliveryDriver");
public static readonly IReadOnlyCollection<Role> Assignable = [Customer, RestaurantManager, DeliveryDriver];
```
Seed in `RoleConfiguration.HasData(...)`. `Administrator` stays unassignable, as today.

### 2.2 Permissions (`Domain/Users/Permission.cs`)
```csharp
public static readonly Permission GetDrivers          = new("drivers:read");
public static readonly Permission ModifyDriver        = new("drivers:update");        // own profile, vehicle, availability, location
public static readonly Permission GetDeliveries       = new("deliveries:read");
public static readonly Permission ManageDeliveries    = new("deliveries:manage");     // accept/reject an offer, picked-up, delivered (own)
public static readonly Permission AdministerDeliveries = new("deliveries:administer"); // admin-only: view/reassign any delivery — the ownership bypass
```

### 2.3 Role → permission seeding (`Infrastructure/Users/PermissionConfiguration.cs`)
- **DeliveryDriver**: `drivers:read`, `drivers:update`, `deliveries:read`, `deliveries:manage`, plus `users:read`/`users:update` for their own profile.
- **Administrator**: all of the above **plus** `deliveries:administer` (the ownership bypass, mirroring how the Orders handlers let an admin act on any restaurant's order). Already holds `users:provision`.
- **Customer**: `deliveries:read` only — needed to track their own order's delivery. Ownership-scoped in the handler; a customer may only read a delivery for an order they placed.
- **RestaurantManager**: nothing new.
- Migration: `Add_Delivery_Role_And_Permissions`.

### 2.4 Generalize the provisioning RPC
`ProvisionManagerUserRequest(Email, FirstName, LastName)` is manager-specific and the role is hard-coded in its consumer. Add the general contract next to it in `Users.IntegrationEvents`:
```csharp
public sealed record ProvisionUserRequest(string Email, string FirstName, string LastName, string Role);
public sealed record ProvisionUserResponse(Guid UserId);
```
`ProvisionUserRequestConsumer` (in `Users.Presentation`, registered in `UsersModule.ConfigureConsumers` with `.Endpoint(c => c.InstanceId = instanceId)`) validates the role against `Role.FromName` and sends `RegisterUserCommand{ Role, RequireInvitation = true }` — the same command the manager consumer already uses. An unknown or non-assignable role returns a failure response, not a 500.

**Leave `ProvisionManagerUserRequest` in place.** Retargeting Restaurants onto the general contract is a mechanical follow-up, and doing it here would drag the Restaurants integration suite into a Delivery PR for no functional gain. Note it as a cleanup.

**Tests**
- *Unit* (`Users.UnitTests` — create the project if it does not exist yet; Restaurants and Notifications have one, Users does not): `Role.FromName("DeliveryDriver")` resolves and is assignable; `Role.FromName("Administrator")` still returns null; `User.Create(..., Role.DeliveryDriver)` assigns the role and raises `UserRegisteredDomainEvent`.
- *Integration* (`Users.IntegrationTests`): `ProvisionUserRequest{ Role = "DeliveryDriver" }` over the bus → response carries a `UserId`, the account is invited (cannot log in), a `UserInvitedIntegrationEvent` is published; `accept-invitation` activates it and the driver can obtain a JWT; `GetUserPermissionsRequest` for that user returns the five delivery permissions and **not** `deliveries:administer`; `ProvisionUserRequest{ Role = "Administrator" }` fails cleanly; a duplicate email fails cleanly.

---

## 3. Milestone B — Delivery service skeleton + `Driver` + onboarding

The largest PR of the set, but it is one coherent unit: a new service that does exactly one thing end to end.

### 3.1 Projects & host
Five module projects (`Domain`, `Application`, `Infrastructure`, `Presentation`, `IntegrationEvents`) under `src/Modules/Delivery/`, plus `src/API/FoodDeliveryService.Delivery.Api` — copy the **Orders** host bootstrap (`Program.cs`, `OpenTelemetry/DiagnosticsConfig.cs` with `ServiceName = "FoodDeliveryService.Delivery"`, `appsettings*.json`, `Dockerfile`). Add all six projects + the two test projects to `FoodDeliveryService.Api.slnx`.

`DeliveryModule.cs` mirrors `OrdersModule.cs`: `AddDomainEventHandlers`, `AddIntegrationEventHandlers`, `AddEndpoints`, `AddDbContext<DeliveryDbContext>` (Npgsql + snake_case + `InsertOutboxMessagesInterceptor`), `IUnitOfWork`, repositories, outbox/inbox Quartz options, `IPermissionService` (the MassTransit RPC one — copy Orders'), `IDeliveryContext`.

### 3.2 Infra wiring
- `docker-compose.yml`: `fooddeliveryservice.delivery.api` (`5500:8080`), same env block as Orders (`ConnectionStrings__Database` → `fooddeliveryservice_delivery`, `Cache`, `Queue`, `Authentication`, OTLP, Seq).
- Database creation: add `fooddeliveryservice_delivery` wherever the other four are created (the Postgres init script / `.containers` bootstrap).
- Gateway `appsettings.Development.json`: `fooddeliveryservice-delivery-route1` → `delivery/{**catch-all}`, `AuthorizationPolicy: "default"`, and a `fooddeliveryservice-delivery-cluster` → `http://fooddeliveryservice.delivery.api:8080`. **No anonymous route** — every delivery endpoint is authenticated.
- Migrations auto-apply via `app.ApplyMigrations()`.

### 3.3 `Driver` aggregate
The driver's profile *is* the aggregate, keyed by `UserId` — there is no separate user replica. Restaurants needed one because `Restaurant` and `RestaurantManager` are different things; here they are the same thing.
```
Id             Guid      // = UserId from Users
Email          string    // snapshot, synced from UserProfileUpdated
FirstName      string
LastName       string
VehicleType    VehicleType   // Bicycle | Motorcycle | Car
Status         DriverStatus  // Offline | Available | Busy
OnboardedOnUtc DateTime
```
- `Driver.Onboard(userId, email, firstName, lastName, VehicleType vehicleType, DateTime utcNow)` → `Status = Offline`, raises `DriverOnboardedDomainEvent`.
- `UpdateProfile(firstName, lastName, vehicleType)` → `DriverProfileUpdatedDomainEvent`.
- `SyncFromUserProfile(email, firstName, lastName)` — called by the `UserProfileUpdated` handler; no-op (and **no event**) when nothing changed.
- Availability transitions land in Milestone C, not here.
- `DriverErrors`: `NotFound(id)`, `NotOnboarded`, `AlreadyOnboarded`, `NotSelf`.

### 3.4 Onboarding + profile endpoints
`OnboardDriverCommandHandler` mirrors `OnboardRestaurantCommandHandler` exactly: `IRequestClient<ProvisionUserRequest>` → Users provisions the invited `DeliveryDriver` account and returns the `UserId` → the handler creates the `Driver` in the same unit of work. Same partial-failure treatment as restaurant onboarding (`RESTAURANTS_PHASE1_PLAN.md` §5.1): compensate via `DeactivateProvisionedUserRequest` — which already exists in `Users.IntegrationEvents` — when the local save fails. Low-frequency, admin-driven, no saga.

| Method & path | Auth | Command/Query | Purpose |
|---|---|---|---|
| `POST delivery/drivers` | `users:provision` (**Administrator**) | `OnboardDriverCommand` | Provision the driver account (RPC → Users, invited) + create the `Driver`. Returns the driver id. |
| `GET delivery/drivers/{id}` | `drivers:read` | `GetDriverQuery` (Dapper) | Driver profile DTO. Self, or admin. |
| `GET delivery/drivers/me` | `drivers:read` | `GetDriverQuery` | Convenience — the caller's own profile. |
| `PUT delivery/drivers/me` | `drivers:update` | `UpdateDriverProfileCommand` | Driver edits their own name/vehicle. Self only. |

Consumers registered in `DeliveryModule.ConfigureConsumers`: `IntegrationEventConsumer<UserProfileUpdatedIntegrationEvent>` → `SyncFromUserProfile`.

**Tests**
- *Unit* (`Delivery.UnitTests`): `Onboard` sets `Offline` and raises `DriverOnboardedDomainEvent`; `UpdateProfile` raises its event; `SyncFromUserProfile` with identical values raises **nothing**; invalid vehicle type rejected.
- *Integration* (`Delivery.IntegrationTests` — copy the Restaurants suite's `IntegrationTestWebAppFactory` + `UsersApiTestFactory` + `Poller`, which already host Users in-process for real JWTs and the permissions RPC): admin `POST delivery/drivers` → `200` + a `drivers` row + an invited account in the Users DB; a **Customer** token → `403`; anonymous → `401`; the invited driver activates, logs in, `GET delivery/drivers/me` → their profile; `PUT delivery/drivers/me` on **another** driver's id → `403`/`NotSelf`; a `UserProfileUpdated` event syncs the driver's name.

---

## 4. Milestone C — Availability & location tracking

### 4.1 Availability
Domain methods on `Driver`, each guarded, each raising an event:

| Method | Allowed from | → | Raises |
|---|---|---|---|
| `GoAvailable()` | Offline | Available | `DriverBecameAvailableDomainEvent` |
| `GoOffline()` | Available | Offline | `DriverWentOfflineDomainEvent` |
| `Reserve()` | Available | Busy | `DriverReservedDomainEvent` *(called on offer accept — Milestone E)* |
| `Release()` | Busy | Available | `DriverReleasedDomainEvent` *(called on delivered/cancel — Milestone F)* |

A `Busy` driver **cannot** go offline mid-delivery → `DriverErrors.OnDelivery`. Any other illegal transition → `DriverErrors.InvalidStatusTransition(from, to)`.

`PATCH delivery/drivers/me/availability` (`drivers:update`, self only) → `SetDriverAvailabilityCommand`.

### 4.2 `IDriverLocationStore` (Application abstraction)
```csharp
public interface IDriverLocationStore
{
    Task RecordAsync(Guid driverId, GeoCoordinate location, DateTime utcNow, CancellationToken ct);
    Task<DriverLocation?> GetCurrentAsync(Guid driverId, CancellationToken ct);
    Task<IReadOnlyCollection<NearbyDriver>> FindNearestAvailableAsync(
        GeoCoordinate origin, double radiusKm, int limit, CancellationToken ct);
    Task EnterAvailablePoolAsync(Guid driverId, CancellationToken ct);
    Task LeaveAvailablePoolAsync(Guid driverId, CancellationToken ct);
}
```
`GeoCoordinate(double Latitude, double Longitude)` is a domain value object that validates its own ranges (−90..90, −180..180) and exposes `DistanceKmTo(other)` (haversine) — a pure, unit-testable function.

### 4.3 Redis implementation (`Infrastructure/Drivers/RedisDriverLocationStore`)
- `GEOADD delivery:drivers:available <lon> <lat> <driverId>` on every position report from an `Available` driver; `ZREM` on `LeaveAvailablePoolAsync` (going offline, or reserved for a delivery). Only available drivers are ever in the set, so the search set stays small.
- `GEOSEARCH delivery:drivers:available FROMLONLAT … BYRADIUS <r> km ASC COUNT <n> WITHCOORD WITHDIST` answers `FindNearestAvailableAsync` in one round trip, already distance-ordered.
- **Staleness:** Redis GEO has no per-member TTL, so a driver who crashes would linger in the set forever at their last position. Alongside the geo entry, write `delivery:driver:{id}:location` (a hash: lat, lon, recordedOnUtc) **with a 60-second TTL**. `FindNearestAvailableAsync` drops any candidate whose location key has expired — a driver who stopped reporting is not a candidate. `GetCurrentAsync` reads the same key.
- Instrument the store with an OTel activity — Redis instrumentation is already registered in `AddInfrastructure`, but the "find nearest" span is worth naming explicitly.

### 4.4 History (Postgres)
`RecordAsync` also appends to `driver_location_history` in the Delivery DB (`driver_id`, `latitude`, `longitude`, `recorded_on_utc`, `delivery_id?`). Straight EF insert, no aggregate — it is an append-only telemetry log, not domain state. At portfolio volume this is fine; note time-partitioning/batching as the scaling lever, and Milestone G as the Cosmos swap.

### 4.5 Endpoint
| Method & path | Auth | Command | Purpose |
|---|---|---|---|
| `POST delivery/drivers/me/location` | `drivers:update` | `RecordDriverLocationCommand` | Called every few seconds by the driver app. Self only. Rejects a location from an `Offline` driver. |

Hot path, called constantly — it does **not** go through the aggregate or the outbox. The handler validates the coordinate, checks the driver's status, and writes to `IDriverLocationStore`. Position reports are not domain events; the delivery's *state* changes are.

**Tests**
- *Unit*: the availability transition table above, including `Busy` → `GoOffline()` failing with `OnDelivery`; `GeoCoordinate` rejecting out-of-range values; `DistanceKmTo` against known city-pair distances (a haversine bug is otherwise invisible until assignment picks the wrong driver).
- *Integration* (real Redis Testcontainer): report a location → `GetCurrentAsync` reads it back; three available drivers at known coordinates → `FindNearestAvailableAsync` returns them **in distance order** and excludes one outside the radius; a driver who goes offline leaves the pool and stops being a candidate; a driver whose location key is expired/absent is excluded even though the geo entry remains; a location report from an offline driver → clean failure; a location report for **another** driver's id → `403`; `driver_location_history` gains a row per report.

---

## 5. Milestone D — Geo contracts + Delivery's replicas

Assignment needs to know **where the restaurant is** and **where the food is going**. Neither coordinate is on the wire today. This milestone is pure contract + replica work across three modules — small, mechanical, and worth isolating so the assignment PR is only about assignment.

### 5.1 Restaurants — put the coordinates on the contract
`Address` already carries `double? Latitude`/`Longitude`, but `RestaurantRegisteredIntegrationEvent` drops them. Add `Latitude`/`Longitude` to the contract and populate them in `RestaurantRegisteredDomainEventHandler`. Since a restaurant with no coordinates can never be assigned a driver, **make them required at onboarding**: tighten `OnboardRestaurantCommandValidator` and the `Address` factory to require lat/long. Add `RestaurantAddressUpdatedIntegrationEvent` (full snapshot) so a moved restaurant propagates.

> Existing restaurants seeded without coordinates need a backfill or a re-onboard in dev. Call this out in the PR; there is no production data.

### 5.2 Orders — coordinates on the delivery address, and on the ready event
- `DeliveryAddress` gains `Latitude`/`Longitude`. Client-supplied at placement (the mobile app has the pin); `PlaceOrderCommandValidator` requires them. Geocoding a free-text address is out of scope — note it as a Phase 3 concern.
- `OrderReadyForPickupIntegrationEvent` (created in the Orders prerequisite, §0) carries the **full snapshot** the assignment routine needs, so Delivery never calls back (hard rule #9): `OrderId`, `CustomerId`, `RestaurantId`, restaurant `Latitude`/`Longitude`, the full delivery address incl. coordinates, and `Subtotal`.

### 5.3 Delivery — replicas
Minimal read models, upserted from integration events, in the Delivery DB:
```
Restaurant:  Id, Name, Latitude, Longitude          // from RestaurantRegistered / RestaurantAddressUpdated
Order:       Id, CustomerId, RestaurantId, DeliveryAddress (owned), PlacedOnUtc   // from OrderReadyForPickup
```
The `Restaurant` replica exists so an admin/support screen can name the pickup point and so a re-offer after a restaurant address change uses the current coordinates. The initial offer uses the coordinates carried on the `OrderReadyForPickup` event; the `Delivery` aggregate snapshots the pickup location so later re-offers don't depend on the event still being around.

Register the consumers in `DeliveryModule.ConfigureConsumers`; handlers in `Delivery.Presentation`; migration `Add_Delivery_Replicas`.

**Tests**
- *Unit*: `Address`/`DeliveryAddress` reject a missing or out-of-range coordinate; `Restaurant.Create` fails without coordinates (`RestaurantErrors.MissingCoordinates`).
- *Integration*: (Restaurants suite) onboarding without coordinates → `400`; the published `RestaurantRegisteredIntegrationEvent` carries them. (Delivery suite) publishing `RestaurantRegisteredIntegrationEvent` → a `restaurants` replica row; an address update moves it; publishing `OrderReadyForPickupIntegrationEvent` → an `orders` replica row with the delivery coordinates intact.

---

## 6. Milestone E — Assignment (`Delivery` aggregate + offer-expiry job)

No message-scheduler infrastructure is needed for this approach — the offer deadline is a column on the `Delivery` aggregate, and Quartz (already wired for outbox/inbox) periodically re-derives which offers have lapsed. Nothing about the RabbitMQ scheduler plugin, `UseDelayedMessageScheduler()`, or a Redis saga repository applies.

### 6.1 `Delivery` aggregate (the record of truth)
```
Id                Guid
OrderId           Guid        // unique — one delivery per order
RestaurantId      Guid
CustomerId        Guid
PickupLocation    GeoCoordinate   // owned — snapshotted so re-offers don't depend on the event
DropoffAddress    DeliveryAddress // owned, incl. coordinates
DriverId          Guid?
Status            DeliveryStatus
OfferedDriverId   Guid?
OfferExpiresOnUtc DateTime?
AssignedOnUtc     DateTime?
PickedUpOnUtc     DateTime?
DeliveredOnUtc    DateTime?
CreatedOnUtc      DateTime
_triedDriverIds   List<Guid>      // private; drivers already offered this delivery, so none is re-offered
```
`enum DeliveryStatus { Pending, Offered, Assigned, PickedUp, Delivered, Unassigned, Cancelled }`

| Method | Allowed from | → | Raises |
|---|---|---|---|
| `Delivery.Create(orderId, restaurantId, customerId, pickup, dropoff, utcNow)` | — | Pending | `DeliveryCreatedDomainEvent` |
| `OfferTo(driverId, expiresOnUtc)` | Pending, Offered | Offered | `DeliveryOfferedDomainEvent` |
| `AcceptOffer(driverId, utcNow)` | Offered | Assigned | `DeliveryAssignedDomainEvent` |
| `RejectOffer(driverId)` | Offered | Pending | `DeliveryOfferRejectedDomainEvent` |
| `ExpireOffer(utcNow)` | Offered | Pending | `DeliveryOfferExpiredDomainEvent` |
| `MarkUnassigned(utcNow)` | Pending, Offered | Unassigned | `DeliveryUnassignedDomainEvent` |
| `MarkPickedUp(driverId, utcNow)` | Assigned | PickedUp | `DeliveryPickedUpDomainEvent` *(Milestone F)* |
| `MarkDelivered(driverId, utcNow)` | PickedUp | Delivered | `DeliveryDeliveredDomainEvent` *(Milestone F)* |
| `Cancel(utcNow)` | any non-terminal | Cancelled | `DeliveryCancelledDomainEvent` *(Milestone F)* |

`OfferTo` adds the driver to `_triedDriverIds` and sets `OfferedDriverId`/`OfferExpiresOnUtc`. `RejectOffer` and `ExpireOffer` clear `OfferedDriverId`/`OfferExpiresOnUtc` and return to `Pending` — the tried list persists, so the same driver is never offered this delivery twice. `AcceptOffer`/`RejectOffer`/`MarkPickedUp`/`MarkDelivered` verify the caller is the offered/assigned driver → `DeliveryErrors.NotAssignedDriver`. `AcceptOffer` past `OfferExpiresOnUtc` → `DeliveryErrors.OfferExpired`. Illegal transitions → `DeliveryErrors.InvalidTransition(from, to)`. `DeliveryErrors` also gets `NotFound(id)`, `NoDriversAvailable`, `AlreadyExists(orderId)`.

**Concurrency:** two orders must never grab the same driver. A unique index on `deliveries.order_id` makes delivery creation idempotent, and `Driver.Reserve()` (Available → Busy) inside the accepting transaction is the guard — the second accept finds the driver already `Busy` and fails cleanly. Redlock/Redis distributed locking is **Feature 2.3**; the aggregate-level guard is correct without it, and this is worth a comment in the code so the 2.3 work doesn't assume it's load-bearing.

### 6.2 The offer routine (one reusable application service)
`IDeliveryAssignmentService.OfferNextAsync(deliveryId, ct)` is the single place the "find the next driver and offer, or give up" logic lives. It is invoked from **three** callers, so it must be idempotent and self-contained:
1. the `OrderReadyForPickup` consumer, right after creating the delivery;
2. the reject command handler, after a driver declines;
3. `ProcessExpiredOffersJob`, after expiring a lapsed offer.

It loads the `Delivery`, calls `IDriverLocationStore.FindNearestAvailableAsync(delivery.PickupLocation, radiusKm, limit)`, drops any candidate already in `_triedDriverIds`, and:
- **a candidate remains** → `delivery.OfferTo(driverId, utcNow + offerWindow)`, save (raises `DeliveryOfferedDomainEvent` → `DeliveryOfferedIntegrationEvent` via the outbox).
- **none remain** → `delivery.MarkUnassigned(utcNow)`, save (→ `DeliveryUnassignedIntegrationEvent`). An admin/support re-offer is a later, manual action.

Offer window (30s), search radius (5km), candidate limit and the job's poll interval are bound options (`Delivery:Assignment` section), **not** constants — the integration tests shrink the window to keep the timeout path fast.

### 6.3 `ProcessExpiredOffersJob` (Quartz)
A recurring Quartz job (registered like `ProcessOutboxJob`/`ProcessInboxJob`, configured via a `ConfigureProcessExpiredOffersJob` options binder) that on each tick:
```
SELECT id FROM deliveries WHERE status = 'Offered' AND offer_expires_on_utc < :utcNow
```
and for each: load the aggregate, `ExpireOffer(utcNow)`, save, then `OfferNextAsync`. Because the expiry condition is a query over Postgres, the job is **stateless and inherently durable** — a service restart loses no timers; the next tick simply re-finds whatever is still expired. The poll interval (e.g. 5s) bounds how long past 30s an offer can linger, so the effective window is `[offerWindow, offerWindow + pollInterval]` — fine for this use, and worth a one-line comment.

> This is the same job-scans-a-table shape as the existing outbox/inbox processors, so it needs no new infrastructure and reads as idiomatic to anyone who's seen those.

### 6.4 Order-cancellation compensation
Register `IntegrationEventConsumer<OrderCancelledIntegrationEvent>` in `DeliveryModule.ConfigureConsumers`; the handler loads the delivery by `OrderId` and calls `Delivery.Cancel(utcNow)` (a no-op if already terminal), which releases any reserved driver back to `Available`. No saga, no timers to unwind — the job simply stops finding a `Cancelled` delivery.

**Tests**
- *Unit*: the full `Delivery` transition table, including accept-after-expiry → `OfferExpired`, accept by a non-offered driver → `NotAssignedDriver`, `OfferTo` refusing a driver already in `_triedDriverIds`, and re-offer from `Offered` after a reject/expiry. The candidate-selection step — nearest first, excluding tried drivers, honouring the radius — is a pure function over the store's result; test it directly.
- *Integration* (real Redis + Postgres Testcontainers, short offer window + fast poll): two available drivers at known distances → publish `OrderReadyForPickup` → the **nearer** driver is offered; they accept → `Assigned`, driver is `Busy` and out of the geo pool, `DriverAssignedIntegrationEvent` published. Nearer driver rejects → the farther one is offered, never the first again. Nearer driver stays silent past the window → `ProcessExpiredOffersJob` expires and re-offers (drive the job directly, or poll). No drivers in radius → `Unassigned` + its event. Two concurrent accepts for one delivery → exactly one wins, the other gets a clean failure. Publishing `OrderCancelledIntegrationEvent` mid-offer → delivery `Cancelled`, driver released to `Available`, and the expiry job leaves it alone thereafter.

---

## 7. Milestone F — Pickup, delivery, and closing the loop in Orders

### 7.1 Delivery endpoints
| Method & path | Auth | Command/Query | Purpose |
|---|---|---|---|
| `POST delivery/deliveries/{id}/accept` | `deliveries:manage` | `AcceptDeliveryOfferCommand` | Offered driver accepts → `Assigned`, driver reserved. Publishes `DeliveryAcceptedIntegrationEvent`. |
| `POST delivery/deliveries/{id}/reject` | `deliveries:manage` | `RejectDeliveryOfferCommand` | Offered driver rejects → the handler re-runs `OfferNextAsync` for the next-nearest candidate. |
| `POST delivery/deliveries/{id}/picked-up` | `deliveries:manage` | `MarkDeliveryPickedUpCommand` | Assigned driver collected the food. |
| `POST delivery/deliveries/{id}/delivered` | `deliveries:manage` | `MarkDeliveryDeliveredCommand` | Delivered; driver released to `Available`, re-enters the geo pool. |
| `GET delivery/deliveries/{id}` | `deliveries:read` | `GetDeliveryQuery` (Dapper) | Delivery DTO + driver name + current driver location. Assigned driver, the order's customer, or an admin. |
| `GET delivery/deliveries` | `deliveries:read` | `GetDeliveriesQuery` (Dapper, paged) | The driver's own delivery history; an admin sees all (`deliveries:administer`). |
| `GET delivery/orders/{orderId}/delivery` | `deliveries:read` | `GetDeliveryByOrderQuery` (Dapper) | The customer's tracking lookup — Feature 2.2 renders this. Customer must own the order. |

> `accept`/`reject`/`picked-up`/`delivered` are the endpoints Milestone E's assignment tests already drive; they ship here with their ownership checks and read models. If Milestone E's PR is getting large, `accept`/`reject` can move forward into it and only the pickup/delivered pair stays here.

### 7.2 Integration events published by Delivery (`Delivery.IntegrationEvents`)
Published from `Application` domain-event handlers via the outbox, mirroring `UserRegisteredDomainEventHandler`. Full snapshots:
- `DeliveryOfferedIntegrationEvent`, `DeliveryAcceptedIntegrationEvent`, `DeliveryOfferRejectedIntegrationEvent`, `DeliveryUnassignedIntegrationEvent` — for Feature 2.2's real-time push and support tooling. (The re-offer loop itself is in-service — the reject handler and the expiry job call `OfferNextAsync` directly, not via these events.)
- `DriverAssignedIntegrationEvent(orderId, deliveryId, driverId, driverFirstName, driverLastName, vehicleType, assignedOnUtc)` — carries the driver's **name** so Notifications can send "your driver is Alex" without calling back.
- `OrderPickedUpIntegrationEvent(orderId, deliveryId, driverId, pickedUpOnUtc)`
- `OrderDeliveredIntegrationEvent(orderId, deliveryId, driverId, deliveredOnUtc)`

### 7.3 Orders closes the loop
`OrderOutForDeliveryDomainEvent` and `OrderDeliveredDomainEvent` are raised by `Order.MarkOutForDelivery()`/`MarkDelivered()` — methods that exist, are unit-tested, and today have **no caller**. This milestone gives them one:
- Register `IntegrationEventConsumer<OrderPickedUpIntegrationEvent>` and `<OrderDeliveredIntegrationEvent>` in `OrdersModule.ConfigureConsumers`.
- `OrderPickedUpIntegrationEventHandler` → `MarkOrderOutForDeliveryCommand` → `order.MarkOutForDelivery()`.
- `OrderDeliveredIntegrationEventHandler` → `MarkOrderDeliveredCommand` → `order.MarkDelivered(utcNow)`.
- These are the **only** callers — no endpoint exposes them, matching `ORDERS_PHASE1_PLAN.md` §4.3. Orders references `Delivery.IntegrationEvents` only (hard rule #4).
- A failed transition (e.g. the order was cancelled concurrently) throws `Common.Application.Exceptions.ApplicationException` so the inbox retries rather than silently dropping — the same convention the other integration handlers use.

**Tests**
- *Unit*: `MarkPickedUp`/`MarkDelivered` by the wrong driver → `NotAssignedDriver`; `MarkDelivered` from `Assigned` (skipping pickup) → `InvalidTransition`; delivered releases the driver.
- *Integration*: the driver marks picked-up then delivered → both integration events land; with the **Orders API hosted in-process** (the Restaurants suite's `OrdersApiTestFactory` already does exactly this for replica assertions), poll the Orders DB and assert the order goes `ReadyForPickup` → `OutForDelivery` → `Delivered`. A non-assigned driver calling `/delivered` → `403`. `GET delivery/orders/{orderId}/delivery` as the ordering customer → `200`; as a different customer → `403`. Reads are Dapper-only.

---

## 8. Milestone G *(optional, portfolio)* — Cosmos DB location store

Nothing above depends on this; it is the Azure/NoSQL showcase, deferred behind the interface on purpose.

Add `CosmosDriverLocationStore : IDriverLocationStore` writing location history to a Cosmos container partitioned by `driverId`, with a `ST_DISTANCE` geospatial query for the nearest-driver read, selected by configuration (`Delivery:LocationStore: "Redis" | "Cosmos"`). Keep Redis GEO as the live pool regardless — a per-position Cosmos write on the hot path is neither cheap nor fast. Integration tests for this path need the Cosmos emulator and should be a separately-marked, non-default test collection so the main suite stays green offline.

---

## 9. Cross-cutting checklist

- **Validation (FluentValidation):** coordinates in range and required; `VehicleType` in enum; radius/limit options positive; driver/delivery ids non-empty. Business rules (illegal transition, wrong driver, expired offer) stay in the domain, not the validator.
- **Observability:** the Delivery host inherits `AddInfrastructure` (OTel + Serilog + EF/Npgsql/Redis/MassTransit instrumentation). Name a span around `FindNearestAvailableAsync`. Offer/assignment transitions and each `ProcessExpiredOffersJob` re-offer are worth logging with `OrderId`/`DeliveryId` in the log context — a stuck delivery is otherwise painful to debug.
- **Hard rules:** reads Dapper-only; writes via `IUnitOfWork.SaveChangesAsync()`; Delivery references only other modules' `*.IntegrationEvents`; integration events are full snapshots; Delivery touches only its own DB.
- **Gateway:** one new `delivery/**` route + cluster; no anonymous routes.
- **Location endpoint load:** `POST delivery/drivers/me/location` at ~1 req/driver/few-seconds is the highest-traffic endpoint in the system. It bypasses the aggregate and the outbox by design. Rate limiting at the gateway (Feature 1.3) needs a higher bucket for this path — note it, don't solve it here.

---

## 10. Definition of done

- `dotnet build` clean; all new migrations apply on startup; `docker-compose up -d` brings up `fooddeliveryservice.delivery.api` and the gateway routes `delivery/**` to it.
- An **Administrator** onboards a driver → an invited `DeliveryDriver` account exists, the invitation email is produced, and the driver activates it and logs in. A Customer or RestaurantManager token is `403` on driver onboarding.
- A driver goes available, reports a location, and appears in the Redis geo pool; going offline removes them; a driver who stops reporting for >60s stops being a candidate.
- Marking an order ready assigns the **nearest available** driver; rejection and timeout both fall through to the next-nearest; no drivers in radius parks the delivery in `Unassigned`. A driver is never offered the same delivery twice. Two concurrent accepts produce exactly one assignment.
- Picked-up → delivered publishes both events, and the `Order` in the **Orders** database ends at `Delivered` — driven entirely over the bus, with no HTTP call between the two services.
- Cancelling an order mid-delivery cancels the delivery and returns the driver to `Available`.
- Offer timeouts survive a Delivery service restart — the deadline is a Postgres column and `ProcessExpiredOffersJob` re-derives what's expired on each tick.
- Unit tests cover the `Driver` and `Delivery` transition tables, the coordinate value object, and the candidate-selection function. Integration tests cover every endpoint's happy path plus its authorization and ownership failures, and the geospatial and assignment/expiry paths against real Redis/RabbitMQ/Postgres containers.
- No hard-rule violations.

---

## 11. Deferred / open (not this iteration)

- **SignalR push of driver location and status to the customer** — Feature 2.2. This plan's `GET delivery/orders/{orderId}/delivery` is the read model it will push.
- **Redlock / distributed locking for assignment** — Feature 2.3. The aggregate-level guard (`Driver.Reserve()` + the unique `order_id` index) is correct today; 2.3 revisits it only if measurement says so.
- **Cosmos DB** — Milestone G, optional.
- **Smarter assignment** — the plan's "later it can factor in driver ratings, current traffic, or order size". Nearest-available only for now; the selection function is isolated so it can grow. Ratings need Feature 2.6.
- **Batching multiple orders to one driver** (stacked deliveries) — real platforms do this; out of scope.
- **Geocoding a free-text address** — coordinates are client-supplied at placement. A geocoding provider is a Phase 3 concern.
- **"Driver marked delivered without moving to the address"** — a fraud signal in Feature 3.4; the location history this milestone writes is exactly its input.
- **Notifications on delivery events** (`DriverAssigned` → "your driver is Alex"; `OrderDelivered` → "rate your order") — the events are published here; the consumers are a Phase 2 Notifications follow-up.
- **Retargeting Restaurants onto `ProvisionUserRequest`** and retiring `ProvisionManagerUserRequest` — mechanical cleanup, deliberately not bundled into a Delivery PR.
- **Driver earnings/payouts** — the `CommissionRate` snapshot on the order is for restaurant commission; driver pay is a separate, unplanned subsystem.
