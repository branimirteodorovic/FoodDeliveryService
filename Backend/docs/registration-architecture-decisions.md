# Registration & Identity Architecture — Design Notes

Date: 2026-07-04

Context: extending FoodDeliveryService beyond Users/Restaurants/Orders to include new actor types — **Delivery Driver** (Delivery microservice) and **Customer Support Agent** (CustomerSupport microservice) — and deciding how registration should work across all actor types (Customer, RestaurantManager, DeliveryDriver, SupportAgent).

## 1. Should every actor type register through the Users service?

**Decision: Yes — Users stays the single identity/role registry for every actor type. Domain services own their own profile data, not identity.**

Rejected approach: having each actor type (restaurant manager, driver, support agent) register *entirely* within its own microservice + Identity, bypassing Users.

Why that was rejected:

- Authorization already depends on centralization. `CustomClaimsTransformation` resolves permissions via `IPermissionService`, which is a request/response call to Users. If every service implements its own registration/permission logic, that RPC pattern fragments into N per-service implementations instead of one.
- Multi-role actors need a single home. A restaurant manager who also wants to order food needs both `RestaurantManager` and `Customer` roles on the *same* account. If identity lives only in the Restaurants DB, there's no natural place to merge roles across services without doing it at auth time — reintroducing the coupling module boundaries are meant to avoid.

**Resulting pattern:**

1. **Identity (Duende)** issues credentials for everyone — unchanged.
2. **Users module** owns the account + role assignment for all actor types (Customer, RestaurantManager, DeliveryDriver, SupportAgent). A single `User` can hold multiple roles.
3. **Domain-specific profile data** lives in the owning service, keyed by `UserId`:
   - Restaurants owns restaurant/business profile fields (business name, license, payout info).
   - Delivery owns driver profile fields (vehicle, license, availability).
   - CustomerSupport owns agent profile fields (queue assignment, shift schedule, etc.).
4. Each domain service reacts to `UserRegisteredIntegrationEvent` to build its own local profile, rather than owning identity itself.
5. The registration *endpoint* the client hits can still be domain-specific (e.g. `restaurants/register`, `delivery/register`) for UX purposes — internally it orchestrates identity creation via Users, then persists domain-specific fields locally.

This preserves: cross-service communication only via the bus, and integration events carrying full snapshots.

## 2. Is Customer a separate entity/microservice, or the same as User?

**Decision: Customer is not a separate microservice. It's the default role on a `User`.**

Unlike RestaurantManager/DeliveryDriver/SupportAgent, "Customer" has no natural owning domain with rich profile data distinct from the base account (name, email, phone, default address) — that data already lives in Users. Orders already keeps a local replica of user data (consuming `UserRegisteredIntegrationEvent` / `UserProfileUpdatedIntegrationEvent`) for order-time needs — this is the same mechanism a hypothetical "Customer service" would need, so introducing a separate service would add no new ownership boundary, just overhead.

**When a dedicated Customer service *would* make sense:** if customer-facing concerns grow into their own bounded context — loyalty programs, subscription tiers, recommendations, saved payment methods with PCI scope isolation. At that point, split it out the same way as Delivery/CustomerSupport: Users still owns identity/role, the new service owns and reacts to the registration event. Premature today.

## 3. If Customer became its own microservice, what's the point of Users vs. Identity — should they merge?

**Decision: Keep Identity and Users separate regardless of how Customer evolves.**

They serve fundamentally different purposes:

- **Identity (Duende)** — generic, protocol-level authentication: credentials, token issuance/validation, OIDC/OAuth flows. Has no knowledge of business roles or domains. This is intentional: keeping the identity provider domain-agnostic limits blast radius if compromised, and lets Duende be upgraded/replaced independently of business logic.
- **Users** — the cross-cutting **role and permission registry**. Even after Customer/Restaurants/Delivery/CustomerSupport each own their own profile data, something still has to answer "what roles does this account have, and what permissions do those roles grant" for `IPermissionService` — that's Users' job, and it requires aggregating role grants across multiple domain services into one coherent claims set. It also stays the orchestration point for registration (provisioning the Identity credential, assigning the initial role, publishing `UserRegisteredIntegrationEvent`).

Merging Users into Identity would couple a security-critical, rarely-changing component (auth) to business rules that change often (roles, permissions, registration flows) — a bad trade for both testability and blast-radius isolation. **Do not merge.**

Net effect after splitting out Customer: Users becomes thinner (loses generic "profile" ownership) but keeps: account/identity record, role assignment across all actor types, permission aggregation, and registration orchestration. It's more accurately described as an "Access & Roles" module at that point rather than a "user profile" module.

## 4. Does the `User` entity have FirstName/LastName? What about restaurant registration?

**Decision: Yes, `User.FirstName`/`LastName` stay required. The mismatch is resolved by not modeling the restaurant business as a `User` at all.**

Every actor who logs in — Customer, RestaurantManager, DeliveryDriver, SupportAgent — is a natural person holding a credential. `FirstName`/`LastName` belong to that person. What doesn't have a personal name is the **restaurant business itself** ("Mario's Pizzeria") — that's a separate aggregate (`Restaurant`) owned entirely by the Restaurants domain, with its own `Name`, `Address`, `Cuisine`, etc., linked back to the managing person via `ManagerUserId`.

Flow:

1. A person registers as a restaurant manager → Users creates a `User` (FirstName, LastName, role `RestaurantManager`).
2. Restaurants creates a `Restaurant` aggregate with `ManagerUserId` pointing at that person.
3. On login, the manager's JWT carries personal identity + `RestaurantManager` role; Restaurants looks up which restaurant(s) they manage via `ManagerUserId` — this also naturally supports one manager running multiple restaurants later, with no change to the identity model.

If a future actor type is genuinely not a person (e.g. a corporate/API-only service account), that's a distinct actor type (`ServiceAccount` or similar), not a reason to make `FirstName`/`LastName` optional on the person-shaped `User` entity.

## 5. How does the Restaurants module collect business details (name, address, cuisine)? One frontend request or two?

**Decision: `UserRegisteredIntegrationEvent` stays identity/role-only — no business-specific fields.** It's a shared contract consumed by multiple services (including Orders), so it should not carry restaurant-specific data.

**Recommended approach — one frontend request, orchestrated synchronously by Restaurants:**

1. Frontend sends a single `POST restaurants/register` containing both personal fields (email, password, first/last name) and business fields (restaurant name, address, cuisine) in one payload.
2. The Restaurants command handler calls Users **synchronously** to provision the identity — a new request/response call over the message bus (`RegisterManagerUserRequest` → `RegisterManagerUserResponse`), following the same mechanical pattern already used for `GetUserPermissionsRequest`. Users runs its normal `RegisterUserCommand` → `IIdentityProviderService` → Duende flow and assigns the `RestaurantManager` role, returning the new `UserId` (or a failure).
3. On success, the same handler creates the local `Restaurant` aggregate (name, address, cuisine, `ManagerUserId`) in the same unit of work.
4. Restaurants then raises its own domain event (`RestaurantRegisteredDomainEvent` → integration event) for any other service that needs to know a new restaurant exists.

This yields one round trip for the frontend and keeps the shared `UserRegisteredIntegrationEvent` clean. Known failure mode: if the Users RPC succeeds but the local `Restaurant` save then fails, an orphaned `User` with the `RestaurantManager` role exists with no restaurant. For a low-frequency operation like registration, this is reasonably handled with either a compensating command back to Users on failure, or a periodic reconciliation job — a full saga is likely overkill for a two-step flow.

**Alternative considered — two frontend requests, fully event-driven:**

`POST users/register` (identity + role only) followed by `POST restaurants/profile` (UserId + business fields) once the UserId is returned. Avoids any new synchronous RPC and stays purely event-driven, but pushes the "what if step two never happens" problem onto the frontend/UX layer (need a "pending" account state, a second form, and abandonment handling). Rejected in favor of the synchronous-orchestration approach for better UX, since registration is inherently an "create two related things atomically" operation.

## 6. A registered user has two ids. Which one does a client get, and how?

Added 2026-09-23, when the Angular client needed it.

**Decision: `GET users/me` is the one place a client learns who it is — the module-side id and the
role names — and it is UI convenience, never an authorization check.**

Registration (§1) creates *two* records for one person: an `ApplicationUser` in Identity, and a
`User` in the Users module holding `IdentityId` as a foreign key. So there are two ids, and they are
not interchangeable:

| Id | Lives in | Appears as | Used by |
|---|---|---|---|
| Identity id | Identity's ASP.NET Identity store | the access token's `sub` claim | Duende, and the `identity_id` column that joins the two |
| Module-side id | `users.id` | `users/register`'s response; the `sub` **custom claim** added per request by `CustomClaimsTransformation` | everything else — `OrderResponse.CustomerId`, `Delivery.DriverId`, the SignalR `user:{id}` group |

Inside the platform this is already handled: `CustomClaimsTransformation` looks the module-side id up
by identity id and adds it as a claim, and `ClaimsPrincipalExtensions.GetUserId()` reads that one, so
no handler ever sees the identity id. A **browser client** has neither of those. It can decode the
token it holds, which gives it the identity id — the one value that matches nothing it will ever be
sent — and it has no way at all to learn its roles, because the API resource declares no `UserClaims`
(the reason for which is `security.md` §6.5) and roles never leave the Users database unasked.

Both gaps are one read, so they are one endpoint: `users/me` returns `userId` (the module-side id),
the profile fields and `roles`. The alternatives were considered and rejected:

- **Mint the roles and the module-side id into the token** (an `IProfileService` in Identity).
  Rejected for the reason §3 keeps the two services apart at all: it makes Identity read the Users
  database. It also puts a role list inside a 15-minute token, where a revocation cannot reach it —
  the exact staleness the permission RPC was chosen to avoid.
- **Let the client infer its id from something it is already sent**, e.g. the first order it places.
  Works until the user has no orders, which is every user on their first screen.
- **Widen `GetUserQuery(Guid)` to carry roles and let the client pass its own id.** It does not have
  its own id — that is half the problem — and it would put a role list behind a caller-supplied id,
  which is an IDOR to write a predicate for rather than a lookup with no parameter at all
  (`security.md` §2).

**The boundary this does not move.** The client reads roles to decide what to render and where to
navigate; no server trusts that. Every permission is still resolved per request from Users and
enforced by `PermissionAuthorizationHandler`. A tampered role in a client's memory changes a menu,
not an answer.

## Summary Table

| Question | Decision |
|---|---|
| Should every actor register via Users? | Yes — Users owns identity + roles for all actor types; domain services own profile data |
| Is Customer its own microservice? | No — Customer is a role on `User`; Orders keeps a local replica as it already does |
| Merge Users into Identity? | No — Identity is protocol-level auth (domain-agnostic); Users is the cross-cutting role/permission registry |
| Does `User` have FirstName/LastName? | Yes, always — every actor is a person; the restaurant business itself is a separate `Restaurant` aggregate in Restaurants, linked via `ManagerUserId` |
| How does Restaurants get business details? | One frontend request to `restaurants/register`; Restaurants synchronously calls Users (RPC) to provision identity, then persists business data locally in the same transaction |
| How does a client learn its own id and roles? | `GET users/me` — returns the **module-side** `users.id` (not the token's `sub`, which is the identity id) plus role names. For rendering and routing only; authorization stays server-side per request |
