# Security

> Feature 3.7 — Final Production Hardening. This document is being built one milestone at a time
> (`HARDENING_PHASE3_PLAN.md`). **Milestone A** contributes the authorization guardrails and the
> ownership sweep below; Milestone I turns it into the consolidated write-up (OWASP pass, secrets,
> database privileges, TLS boundary, known limitations).

The rule this feature works to: **do not write a security checklist, write a test that fails when
the property is violated.** A checklist is accurate on the day it is written. Everything below that
can be asserted, is — and the "Guardrail" column says where.

## 1. Authorization model

Authorization is **permission-based**, not role-based. A JWT from Duende carries the subject and
nothing about what they may do; each service resolves the caller's permission codes from the Users
service over MassTransit RPC (`GetUserPermissionsRequest`), cached in Redis for five minutes, and
`CustomClaimsTransformation` puts them on the principal. An endpoint names a permission code as its
policy string, and `PermissionAuthorizationPolicyProvider` manufactures the matching
`PermissionRequirement` on demand — which is why the policy name *is* the permission code, and why a
typo in it produces a policy nobody can ever satisfy.

Defence in depth is two layers, not three: the Gateway validates the JWT and rejects anonymous
traffic on every route but the two registration paths; the service validates it again and evaluates
the permission. The Gateway does **not** know permissions, so it cannot shed an unauthorized request
before proxying it.

| Property | Guardrail |
|---|---|
| Every endpoint carries a permission policy or an explicit `AllowAnonymous` | `Common.UnitTests/Security/EndpointAuthorizationTests.cs` |
| The anonymous surface is exactly `users/register` + `users/accept-invitation` | same file — an allow-list asserted in both directions |
| Every policy string names a permission the Users module actually seeds | same file, against `Users.Domain/Users/Permission.cs` |
| Every module's `Permissions` constant names a seeded code | same file |
| The three health probes are anonymous, and nothing else in that mapping is | same file |
| Every YARP route names a defined cluster, and carries an authorization policy | `Common.UnitTests/Security/GatewayRouteTests.cs` |
| Every module path prefix has a gateway route (hard rule #10) | same file |
| The compose and Kubernetes routing tables have not drifted apart | same file, against `deploy/k8s/services/gateway.yaml` |

The tests build each module's **real** route table through the same `AddEndpoints` reflection the
hosts use, so they assert what would actually be served. They run in `Common.UnitTests`, which is
the first entry in CI's project list, so they gate every pull request.

## 2. Ownership and IDOR

Permission codes answer "may this kind of caller do this kind of thing". They do not answer "is this
*their* order", and every endpoint that takes an id needs the second answer too. Two conventions
apply:

- **Ownership is scoped to the caller's id** from the module's `I{Module}Context`, with an
  **administrator bypass** recognized by an admin-only permission the ordinary role never holds
  (`restaurants:create` for Orders and Restaurants, `deliveries:administer` for Delivery,
  `support-tickets:manage` / `support-tickets:administer` for Support).
- **404, not 403, when the resource is not the caller's.** A 403 — or any status that differs from
  the one for an id that does not exist — confirms the resource exists, which is the one thing an
  attacker enumerating ids is trying to learn. Every scoped **read** carries its ownership predicate
  *in the `WHERE` clause* for that reason (§2.2); writes still branch after the read (§2.3).

### 2.1 The sweep

Every endpoint that takes an id, what scopes it, and what a caller who is not entitled to it gets.

| Endpoint | Scoped by | Denied with |
|---|---|---|
| `GET orders/{id}` | ownership **in the SQL**: `AND (@IsAdmin OR o.customer_id = @UserId OR r.manager_user_id = @UserId)` | **404** ✔ |
| `GET orders` | `WHERE` on customer / manager / admin | empty list |
| `POST orders/{id}/cancel` | the order's customer, and only them — no admin bypass | 400 `Orders.NotOwner` |
| `POST orders/{id}/{accept,reject,preparing,ready}` | `OrderOwnership` — the restaurant's manager or admin | 400 `Orders.NotOwner` |
| `GET delivery/deliveries/{id}` | ownership **in the SQL**: `DeliveryAccess.VisibleToCallerSql` — customer, assigned driver, or admin | **404** ✔ |
| `GET delivery/orders/{orderId}/delivery` | the same predicate | **404** ✔ |
| `GET delivery/deliveries` | `WHERE` on driver / admin | empty list |
| `GET delivery/drivers/{id}` | self-or-admin, checked **before** the read | 400 `Drivers.NotSelf` — leaks nothing, the row is never read |
| `GET delivery/drivers/me`, `PUT/PATCH/POST delivery/drivers/me/*` | the route is the caller — no id to confuse | n/a |
| `POST delivery/deliveries/{id}/{accept,reject,picked-up,delivered}` | the offer/assignment is matched to the calling driver in the aggregate | 400 / 409 from the aggregate |
| `GET restaurants`, `GET restaurants/{id}`, `GET restaurants/{id}/menu` | **not** ownership-scoped, by design — the catalogue is readable by any authenticated caller (`restaurants:read` / `menu:read`) | 404 if absent |
| `PUT restaurants/{id}`, all `menu-categories` / `menu-items` writes | `RestaurantOwnership.EnsureCanModify` — owning manager or admin | 400 `Restaurants.NotManager` |
| `GET support/tickets/{id}` | ownership **in the SQL**: `WHERE t.id = @TicketId AND (@IsStaff OR t.customer_id = @UserId)` | **404** ✔ |
| `GET support/tickets/{id}/messages` | the same predicate, plus internal notes filtered in SQL for non-staff | **404** ✔ |
| `GET support/tickets` | the same predicate over the list | empty list |
| `GET support/tickets/{id}/audit` | staff-only (`support-tickets:manage`) — no customer-facing path exists | 404 if absent |
| `POST support/tickets/{id}/{status,assign,claim,unassign,messages}` | staff permission plus the aggregate's own rules (segregation of duties on refunds) | 400 / 403 / 404 per rule |
| `GET support/refund-requests`, `GET support/analytics/summary` | staff-only aggregates over every ticket — deliberately not caller-scoped | n/a |

No handler was found scoping by **nothing**. Everything that takes an id either checks ownership,
filters by it in SQL, or is deliberately open to all authenticated callers (the restaurant
catalogue).

### 2.2 Reads: the predicate is in the query, not in a branch

All four ownership-scoped single-resource reads — Orders, the two Delivery reads and Support's
ticket — now put the ownership predicate **in the `WHERE` clause**. That is what makes the 404
unfakeable: a caller who is not entitled to the row does not get one, so there is no code path on
which a distinguishable "not yours" could be returned instead. Where the check used to sit after
the read (Orders and Delivery returned `Error.Problem` → HTTP 400, which told a caller that a
guessed id was real), it no longer does.

The shared predicates live in one place per module so the read paths cannot drift onto different
definitions of "yours": `DeliveryAccess.VisibleToCallerSql` and `TicketAccess.IsStaff`. Orders
inlines its own — it has a single scoped read.

Covered end-to-end by `Delivery.IntegrationTests/Deliveries/PickupDeliveryTests` — an assigned
driver and an administrator get the tracking view, a bystanding driver gets a 404. Orders has no
equivalent test: its integration suite has one seeded principal, an Administrator, who bypasses
ownership by definition, so a non-owner read cannot be expressed without a second real Duende
identity in the fixture.

### 2.3 Writes still answer 400 on an ownership failure

`OrderOwnership.EnsureCanManage`, `RestaurantOwnership.EnsureCanModify` and `CancelOrder`'s
customer check all return `Error.Problem` → **HTTP 400** with a `NotOwner` / `NotManager` code, which
distinguishes a real id from an absent one exactly as the reads used to. The exposure is the same
class and the reasoning for changing it is the same; it is left standing here because it touches a
dozen endpoints across two shared guards and belongs with Milestone F's error-surface pass (§7.4 of
`HARDENING_PHASE3_PLAN.md`) rather than with the read fix.

## 3. What Milestone A does not cover

Named so a reader does not mistake this page for the finished document: secrets handling (Milestone
B), database privileges (C), security headers / forwarded headers / CORS (D), Identity key management
and lockout (E), input validation and the error surface (F), API documentation reachability (G),
supply-chain scanning (H), and the consolidated OWASP pass, TLS boundary and known-limitations
sections (I).
