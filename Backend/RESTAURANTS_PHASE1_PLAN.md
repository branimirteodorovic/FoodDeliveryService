# Phase 1 — Restaurant Onboarding & Menu Setup — Implementation Plan

> Scope for this iteration: an **Administrator** can onboard a restaurant — creating the `Restaurant` and its `RestaurantManager` login — and the manager, after accepting an emailed invitation, can **build their menu**. No public self-registration for staff/partner roles. The platform **commission rate is set by the Administrator per restaurant at onboarding** (no fixed default). The ordering flow is out of scope (it will consume this data later).

Decisions locked in for this plan:
- **Staff/partner accounts are admin-provisioned, not self-registered.** Restaurant managers (and, later, delivery drivers and support agents) do **not** create their own accounts: restaurants sign a contract with the platform, and drivers/support are employees. An **Administrator** creates these accounts; the invitee receives an **invitation email** with a one-time **activation link (token)** and sets their own password to activate (see §5.3). **No plaintext temporary password is ever emailed.** Only **Customers** (`Customer` role) self-register.
- **Initial Administrator is seeded from configuration.** No one can self-register as admin, so the first admin account is seeded on startup from `appsettings` (email + password). Defaults live in `appsettings.Development.json` (`admin@fooddeliveryservice.com` / `admin`); `appsettings.json` holds **empty** values so real environments must supply their own (see §7).
- **Single admin action onboards a restaurant.** One authenticated `POST restaurants` (Administrator only) carries business fields (including the **commission rate**) + the manager's contact details. The Restaurants handler calls Users **synchronously over the bus** (`ProvisionManagerUserRequest`/`Response`, mechanically identical to `GetUserPermissionsRequest`) to create the manager identity + assign the `RestaurantManager` role, then persists the `Restaurant` in the same unit of work. Only Users talks to Identity (hard rule #4).
- **Menu depth = Categories + Items** — modifiers/options deferred.
- Reference implementation to mirror throughout: the **Users** module.

> **Revision history:** v1 assumed anonymous self-registration for managers (`users/register` with a role); v2 switched to single-call synchronous orchestration. **v3 (this doc)** removes self-registration for managers entirely — accounts are created by an Administrator and activated via email invitation. This is the platform-wide model for every non-customer actor.

---

## 1. Architecture overview

Modules touched this iteration, each owning its own state and database:

| Module | Responsibility |
|---|---|
| **Users** (`fooddeliveryservice_users`) | Owns identity/role for all actors. Add `RestaurantManager` role + restaurant/menu permissions; assign account-provisioning permissions to `Administrator`. Add the `ProvisionManagerUserRequest` RPC consumer (creates an invited account — no password — with a one-time activation token). Add a set-password / accept-invitation flow. Carry roles in `UserRegisteredIntegrationEvent`; publish a `UserInvitedIntegrationEvent`. |
| **Restaurants** (`fooddeliveryservice_restaurants`) | Owns `Restaurant`, `MenuCategory`, `MenuItem`. Orchestrates admin onboarding (RPC to Users → create `Restaurant`). Keeps a local manager replica. Exposes onboarding + menu endpoints. |
| **Identity** (Duende host) | Creates the account with **no usable password** in an inactive/"must change password" state; issues the one-time invitation token consumed at activation. |
| **Notifications** (`fooddeliveryservice_notifications`) | Consumes `UserInvitedIntegrationEvent` and sends the invitation email. |

Cross-service contact stays on the bus: Restaurants calls Users via `ProvisionManagerUserRequest` (request/response) and consumes `UserRegisteredIntegrationEvent` for its local replica; Notifications consumes `UserInvitedIntegrationEvent`. No service touches another's DB or API directly. Authorization resolves permissions via the existing `GetUserPermissionsRequest` RPC to Users (cached in Redis).

> **Why Users stays the single identity/role registry for every actor.** Authorization already centralizes on Users (`IPermissionService` → `GetUserPermissionsRequest`); letting each service run its own account/permission logic would fragment that into N implementations. Multi-role actors also need one home — a manager who also orders food holds both `RestaurantManager` and `Customer` on the *same* account. So identity + role assignment for **all** actor types lives in Users; each domain service owns only its own profile data, keyed by `UserId`, built by reacting to `UserRegisteredIntegrationEvent`. Identity (Duende) and Users stay **separate** — protocol-level auth vs. cross-cutting role/permission registry.

**End-to-end flow (restaurant onboarding)**

1. An **Administrator** (authenticated) → `POST restaurants` with business fields + manager contact (email, first/last name) in one payload.
2. Restaurants `OnboardRestaurantCommand` handler → sends `ProvisionManagerUserRequest` to Users → Users provisions the Identity account with **no usable password** in an inactive/"must change password" state, assigns the `RestaurantManager` role, returns the new `UserId` (or a failure). Users raises `UserRegisteredDomainEvent` (→ `UserRegisteredIntegrationEvent`, roles included) and a `UserInvitedIntegrationEvent` carrying the one-time activation token.
3. On success, the same handler creates the `Restaurant` aggregate (`ManagerUserId` = returned id, business details, address, cuisine, `CommissionRate` = the admin-supplied rate, `Status = Active`) and commits in one unit of work. It raises `RestaurantRegisteredDomainEvent` → `RestaurantRegisteredIntegrationEvent`.
4. **Notifications** consumes `UserInvitedIntegrationEvent` → sends the invitation email containing the activation link (token only — no password).
5. **Restaurants** consumes `UserRegisteredIntegrationEvent` → upserts a local `RestaurantManager` replica (name/email).
6. The manager clicks the link → sets a new password (§5.3) → logs in via Identity (public client) → JWT. `CustomClaimsTransformation` resolves the manager's permissions from Users.
7. `POST restaurants/{restaurantId}/menu-categories` then `POST .../menu-items` (auth: `menu:manage`) → manager builds the menu. Writes are ownership-checked (only the owning manager may modify).

---

## 2. Users module changes

### 2.1 Roles (`Domain/Users/Role.cs`)
`Administrator` and `Member` already exist. **Rename `Member` → `Customer`** (update the static `Role.Member` definition, its `RoleConfiguration.HasData` seed, and any references; add a migration for the renamed seed row). Then add:
```csharp
public static readonly Role Customer = new("Customer");           // was Member
public static readonly Role RestaurantManager = new("RestaurantManager");
// Later iterations: DeliveryDriver, SupportAgent
```
Seed all roles in `RoleConfiguration.HasData(...)`.

> `User.Create` currently hard-codes `Role.Member` (now `Role.Customer`). Change it to accept the role to assign (see §2.4).

### 2.2 Permissions (`Domain/Users/Permission.cs`)
Add the restaurant/menu set plus an account-provisioning permission (leave the leftover event-ticketing codes for a later cleanup):
```csharp
public static readonly Permission GetRestaurants   = new("restaurants:read");
public static readonly Permission CreateRestaurant = new("restaurants:create");   // = onboard restaurant + manager
public static readonly Permission ModifyRestaurant = new("restaurants:update");
public static readonly Permission ManageMenu       = new("menu:manage");
public static readonly Permission GetMenu          = new("menu:read");
public static readonly Permission ProvisionUsers   = new("users:provision");      // create staff/partner accounts
```

### 2.3 Role → permission seeding (`Infrastructure/Users/PermissionConfiguration.cs`)
Register the new permissions in `HasData` and assign them by role — note that **create/provision rights belong to Administrator, not to the manager**:
- **Administrator**: `ProvisionUsers`, `CreateRestaurant`, `GetRestaurants`, `ModifyRestaurant`, `GetMenu`, `ManageMenu` (full oversight), plus existing user perms.
- **RestaurantManager**: `GetRestaurants`, `ModifyRestaurant`, `ManageMenu`, `GetMenu` (their own restaurant only — enforced by ownership check), plus `GetUser`/`ModifyUser` for their own profile. **No** `CreateRestaurant`/`ProvisionUsers`.
- **Customer**: `GetRestaurants`, `GetMenu` (read-only browsing; full browse endpoints arrive with the ordering work).

### 2.4 Create-user-with-role, with optional invitation (Application)
Extend registration to support both self-service (customer, real password) and admin-provisioned (staff, invitation-token — no password):
- `RegisterUserCommand` — add `string Role` (default `Customer`) and a `bool RequireInvitation` (default `false`). When `RequireInvitation` is true, no caller-supplied password is used.
- `User.Create(email, firstName, lastName, identityId, Role role)` — assign the passed role instead of hard-coding `Customer`.
- `RegisterUserCommandHandler` — for invited accounts, ask `IIdentityProviderService` to create the identity in an inactive/`MustChangePassword` state (no usable password) and obtain a one-time activation token; then create the module `User`, raise `UserRegisteredDomainEvent`, and emit the data needed for `UserInvitedIntegrationEvent` (token + expiry).
- `RegisterUserCommandValidator` — role is one of the allowed values; for self-service, password rules apply; for invited, password must be absent.
- `IIdentityProviderService` — extend with an invited-provisioning method returning the identity id + activation token (Identity generates both; see §7 and §5.3).

### 2.5 `ProvisionManagerUserRequest` RPC consumer (Presentation) — new
The synchronous entry point the Restaurants module calls to provision a manager account. Mirror `GetUserPermissionsRequestConsumer` (a MassTransit `IConsumer` in `Users.Presentation`, registered via `UsersModule.ConfigureConsumers` with `.Endpoint(c => c.InstanceId = instanceId)`):
- Contract in `Users.IntegrationEvents`: `ProvisionManagerUserRequest(Email, FirstName, LastName)` → `ProvisionManagerUserResponse(Guid UserId)` (+ failure channel — return a result-shaped response so duplicate-email/validation failures surface to the caller).
- `ProvisionManagerUserRequestConsumer` → `ISender.Send(new RegisterUserCommand(Email, FirstName, LastName, Role: "RestaurantManager", RequireInvitation: true))` → returns the new `UserId` or the failure.

> Generalizes: DeliveryDriver / SupportAgent add sibling contracts or share one `ProvisionUserRequest(..., Role)` — same mechanism, all admin-triggered.

### 2.6 Set-password / accept-invitation endpoint (Presentation)
Invited accounts start unusable until the invitee sets a password. Add an anonymous endpoint that consumes the activation token:
- `POST users/accept-invitation` → `AcceptInvitationCommand(Token, Email, NewPassword)` → `IIdentityProviderService` validates the token, sets the password, clears "must change password". Anonymous (the user has no session yet).
- Alternatively/additionally `POST users/change-password` (authenticated) for later self-service password changes.

### 2.7 Customer self-registration endpoint (Presentation)
There is currently **no** `IEndpoint` in `Users.Presentation` (only the `GetUserPermissionsRequestConsumer`). Add the customer path (the **only** anonymous account creation):
- `RegisterUser` → `POST users/register`, anonymous, `RegisterUserCommand{ Role="Customer", RequireInvitation=false }`. Managers/staff never use this path.

### 2.8 Integration events
- `UserRegisteredIntegrationEvent` — add `IReadOnlyCollection<string> Roles` (identity/role snapshot only; no business fields — it's shared with Orders/Restaurants). Populate in `UserRegisteredDomainEventHandler`.
- `UserInvitedIntegrationEvent` — **new**, in `Users.IntegrationEvents`: `(UserId, Email, FirstName, LastName, ActivationToken, ExpiresOnUtc)`. Published for invited accounts so Notifications can build the activation link and send the email. Carries the one-time token only — never a password (§5.3).

---

## 3. Restaurants module — entities

All aggregates extend `Entity`, use a private constructor + `private set` + static factory, keep business logic in the domain, and raise a domain event on every state change. Mirror `Users/User.cs`.

### 3.1 `Restaurant` (aggregate root) — replace the current stub
```
Id                Guid
ManagerUserId     Guid        // the RestaurantManager user id (returned by the RPC)
Name              string      // company / trading name
TaxIdentification string      // company registration / tax number
CuisineType       string      // e.g. "Italian", "Sushi"  (enum or free text)
Email             string      // business contact
PhoneNumber       string
Address           Address     // owned value object (see 3.4)
CommissionRate    decimal     // admin-supplied at creation; fraction 0..1 (e.g. 0.20 = 20%)
Status            RestaurantStatus  // Active on create (no approval step)
CreatedOnUtc      DateTime
```
Factory: `Restaurant.Create(managerUserId, name, taxId, cuisineType, email, phone, Address address, decimal commissionRate)` → validates `commissionRate` is in range and sets it, `Status = Active`, raises `RestaurantRegisteredDomainEvent`. `ManagerUserId` is **not** unique — one manager may run multiple restaurants. If `commissionRate` is out of range, return `Result.Failure(RestaurantErrors.InvalidCommissionRate)` (the factory returns `Result<Restaurant>`).
Behavior: `UpdateDetails(...)`, `UpdateAddress(...)` (each raises a domain event, returns `Result`).

### 3.2 `MenuCategory` (child of Restaurant)
```
Id            Guid
RestaurantId  Guid
Name          string      // "Starters", "Mains", "Desserts"
DisplayOrder  int
```
Factory `MenuCategory.Create(restaurantId, name, displayOrder)`; `Rename(...)`, `Reorder(...)`.

### 3.3 `MenuItem` (child of a category)
```
Id            Guid
RestaurantId  Guid
CategoryId    Guid
Name          string
Description   string
Price         decimal     // + Currency (or a Money value object)
PhotoUrl      string?     // URL only; upload/photography flow out of scope
IsAvailable   bool        // "available / sold out"
```
Factory `MenuItem.Create(...)`; behavior `UpdateDetails(...)`, `ChangePrice(...)`, `SetAvailability(bool)`.

### 3.4 Value objects
- `Address(Street, City, PostalCode, Country, Latitude?, Longitude?)` — owned by `Restaurant` (`OwnsOne`). Lat/long optional now; useful for delivery-zone work later.
- `Money(Amount, Currency)` — optional; a plain `decimal Price` + default currency is fine for this iteration.

### 3.5 `RestaurantManager` (local user replica)
Populated asynchronously from `UserRegisteredIntegrationEvent` (same pattern Orders uses). Minimal:
```
Id (= UserId)   Guid
Email           string
FirstName       string
LastName        string
```
The `Restaurant` only stores `ManagerUserId`, so its creation doesn't block on this replica. Used to attribute/display the manager without querying the Users DB (hard rule #5).

### 3.6 Errors & domain events
- `RestaurantErrors` — `NotFound(id)`, `NotManager` (a manager may own multiple restaurants, so there is **no** one-per-manager uniqueness rule), `InvalidCommissionRate`.
- `MenuCategoryErrors`, `MenuItemErrors` — `NotFound`, `InvalidPrice`, `DuplicateName`, etc.
- Domain events: `RestaurantRegisteredDomainEvent`, `RestaurantDetailsUpdatedDomainEvent`, `MenuCategoryAddedDomainEvent`, `MenuItemAddedDomainEvent`, `MenuItemAvailabilityChangedDomainEvent`.

### 3.7 Persistence
- `RestaurantsDbContext`: add `DbSet<Restaurant>`, `DbSet<MenuCategory>`, `DbSet<MenuItem>`, `DbSet<RestaurantManager>` and `ApplyConfigurationsFromAssembly` (keep the existing outbox/inbox configs).
- One EF `IEntityTypeConfiguration` per entity (snake_case tables). `OwnsOne(Address)`.
- New EF migration `Add_Restaurants_And_Menu` (auto-applied via `app.ApplyMigrations()`).
- Repositories: expand `IRestaurantsRepository` (`GetAsync`, `GetByManagerAsync`, `Insert`) and add menu access (or fold categories/items into the aggregate).

---

## 4. Endpoints

Commands = EF + `IUnitOfWork.SaveChangesAsync()`; queries = Dapper via `IDbConnectionFactory` (hard rule #2). Every endpoint is an `IEndpoint`, returns `result.Match(Results.Ok, ApiResults.Problem)`, tags the group (`.WithTags("Restaurants")` / `"Users"`), and `.RequireAuthorization(<permission>)` unless marked anonymous.

Gateway impact is small: the authenticated `restaurants/{**catch-all}` and `users/{**catch-all}` routes already cover the paths below. `users/register` already has its own anonymous route. **Add one anonymous route: `users/accept-invitation`** (invitees have no JWT yet). No anonymous `restaurants/register` is needed anymore — onboarding is authenticated.

> The `Tags` / `Permissions` static-constant helpers shown in `CLAUDE.md`'s example don't exist in this repo yet. Create small constant classes per module or use string literals; permission strings must match the codes seeded in Users (§2.2).

### Users module
| Surface | Auth | Command/Query | Purpose |
|---|---|---|---|
| RPC `ProvisionManagerUserRequest` (bus) | internal | `RegisterUserCommand{ Role="RestaurantManager", RequireInvitation=true }` | Provision a manager account on behalf of Restaurants; return `UserId`; trigger invitation. |
| `POST users/register` | anonymous | `RegisterUserCommand{ Role="Customer" }` | **Customer** self-registration only. |
| `POST users/accept-invitation` | anonymous | `AcceptInvitationCommand` | Invitee sets their password via the emailed token; activates the account. |
| `POST users/change-password` (optional) | authenticated | `ChangePasswordCommand` | Later self-service password change. |

### Restaurants module
| Method & path | Auth (permission) | Command/Query | Purpose |
|---|---|---|---|
| `POST restaurants` | `restaurants:create` (**Administrator**) | `OnboardRestaurantCommand` | Admin onboards a restaurant: business fields (incl. **commission rate**) + manager contact. Handler RPCs Users to provision the manager account (invited), then creates the `Restaurant` (`ManagerUserId`, admin-supplied `CommissionRate`, `Status=Active`) in one unit of work. |
| `GET restaurants/{id}` | `restaurants:read` | `GetRestaurantQuery` (Dapper) | Fetch a restaurant profile (DTO, never the entity). |
| `GET restaurants` | `restaurants:read` | `GetRestaurantsQuery` (Dapper, paged) | List/basic browse (search/filter can be minimal here). |
| `PUT restaurants/{id}` | `restaurants:update` | `UpdateRestaurantCommand` | Update details/address. Ownership-checked (admin may also edit). |
| `POST restaurants/{id}/menu-categories` | `menu:manage` | `CreateMenuCategoryCommand` | Add a category. Ownership-checked. |
| `PUT menu-categories/{categoryId}` | `menu:manage` | `UpdateMenuCategoryCommand` | Rename/reorder. |
| `POST restaurants/{id}/menu-items` | `menu:manage` | `CreateMenuItemCommand` | Add an item (name, description, price, photo URL, availability). |
| `PUT menu-items/{itemId}` | `menu:manage` | `UpdateMenuItemCommand` | Edit item details/price. |
| `PATCH menu-items/{itemId}/availability` | `menu:manage` | `SetMenuItemAvailabilityCommand` | Mark available / sold out. |
| `GET restaurants/{id}/menu` | `menu:read` | `GetMenuQuery` (Dapper) | Full menu (categories + items) for the storefront. |

**Ownership enforcement**: menu/profile write handlers load the restaurant and compare `restaurant.ManagerUserId` to `IRestaurantsContext` current user id; mismatch → `Result.Failure(RestaurantErrors.NotManager)` (403). Administrators bypass the ownership check (they may edit any restaurant). This satisfies the phase-1 requirement that only the owning restaurant (or an admin) can modify its data.

> Minor cleanup: `IRestaurantsContext.NotificationId` is a scaffold copy-paste — rename to `UserId` (it already returns `User.GetUserId()`).

---

## 5. Cross-service messaging

### 5.1 Synchronous RPC — `ProvisionManagerUserRequest` (Restaurants → Users)
Same mechanism as `GetUserPermissionsRequest`:
- Contract in `Users.IntegrationEvents` (Restaurants references only that project — hard rule #4): `ProvisionManagerUserRequest(Email, FirstName, LastName)` → `ProvisionManagerUserResponse` carrying either the new `UserId` or a failure (error code + message, so duplicate-email/validation failures become a proper problem response, not a 500).
- Restaurants' `OnboardRestaurantCommandHandler` injects `IRequestClient<ProvisionManagerUserRequest>`, awaits the response, and short-circuits with `Result.Failure(...)` on failure.
- Consumer `ProvisionManagerUserRequestConsumer` in `Users.Presentation` runs `RegisterUserCommand` (invited) and replies.

**Partial-failure handling.** If the RPC succeeds (account created) but the local `Restaurant` save then fails, an orphaned invited `RestaurantManager` account with no restaurant remains. Because onboarding is low-frequency and admin-driven, handle **without a saga**: either (a) send a compensating `DeactivateUser` request to Users on the save failure, or (b) run a periodic reconciliation job removing invited manager accounts with no restaurant past a grace period. Note the choice; a saga is overkill for a two-step flow.

### 5.2 Async events consumed by Restaurants (already registered)
`UserRegisteredIntegrationEvent`, `UserProfileUpdatedIntegrationEvent` are wired in `RestaurantsModule.ConfigureConsumers`. Add the missing `IIntegrationEventHandler<T>` in `Restaurants.Presentation`:
- `UserRegisteredIntegrationEventHandler` → upsert the `RestaurantManager` replica.
- `UserProfileUpdatedIntegrationEventHandler` → keep the replica's name in sync.

### 5.3 Invitation → email (Users → Notifications) and account activation
- Users publishes `UserInvitedIntegrationEvent` (§2.8) for invited accounts.
- **Notifications** consumes it and sends the invitation email. Register `IntegrationEventConsumer<UserInvitedIntegrationEvent>` in `NotificationsModule.ConfigureConsumers` (currently empty) and add a `UserInvitedIntegrationEventHandler` in `Notifications.Presentation` (its assembly is scanned by `AddIntegrationEventHandlers`). The Notifications module also needs an `IEmailService` (SMTP for local dev per the phase-1 plan; SendGrid later) — for local dev it can log the email / write to Seq.
- **Activation mechanism — token link (locked in).** On provisioning, Identity creates the account in an inactive/`MustChangePassword` state with **no usable password** and generates a one-time invitation token (`UserManager.GeneratePasswordResetTokenAsync`). The `UserInvitedIntegrationEvent` carries the token + expiry; the email contains an activation link (e.g. `…/accept-invitation?token=…&email=…`). The invitee sets their own password via `POST users/accept-invitation`, which validates the token (`ResetPasswordAsync`) and clears the inactive flag. **No plaintext password is ever generated, emailed, or stored in an event.** The token expires (use ASP.NET Identity's data-protection token lifespan, e.g. a few days); expired links require the admin to resend/re-issue the invitation.

### 5.4 Async events published by Restaurants
Add `Restaurants.IntegrationEvents` contract `RestaurantRegisteredIntegrationEvent` (id, name, cuisine, address, commissionRate) so future Orders/Notifications modules can react. Wire `RestaurantRegisteredDomainEventHandler` → `IEventBus.PublishAsync(...)` via the outbox, mirroring `UserRegisteredDomainEventHandler`. Publishing can be stubbed now.

---

## 6. Commission

The commission rate is **provided by the Administrator on the onboarding request** and stored on the `Restaurant` aggregate — it's a per-restaurant commercial term, negotiated in the off-platform contract, so it varies by restaurant (no fixed default). Represent it as a `decimal` **fraction** (e.g. `0.20` = 20%), not an int percentage, to avoid rounding ambiguity. Validate the range at both the FluentValidation layer (`OnboardRestaurantCommand`) and the domain factory (`RestaurantErrors.InvalidCommissionRate`): require `0 ≤ CommissionRate < 1` (optionally cap at a sane maximum such as `0.5`). The **Order** service reads it later to split each order total — no order-side work this iteration, but persisting it now means the ordering work has it ready. Updating a restaurant's commission after onboarding is out of scope here (admin-only, later).

---

## 7. Cross-cutting / infra checklist

- **Identity** (`ApplicationUser`, `UserEndpoints`, `Config.cs`):
  - `ApplicationUser` currently has only `FirstName`/`LastName`. Add a `MustChangePassword` (or `IsInvited`/`ActivatedOnUtc`) flag to represent an un-activated invited account.
  - Extend the local `api/users` provisioning surface (or add `api/users/invite`) to create an invited user **with no usable password** + the flag, and return a one-time invitation token (`UserManager.GeneratePasswordResetTokenAsync`). Add an endpoint to consume that token and set the password (`ResetPasswordAsync`), clearing the flag. No temporary password is generated.
  - No client/scope change needed for managers: they use the existing public client + `fooddeliveryservice.api` scope; elevated rights come from the `RestaurantManager` permissions resolved via Users, not token scopes.
- **Admin bootstrap (seed from configuration):** since no one can self-register as `Administrator`, seed an initial admin on startup from `appsettings`. Bind an options section, e.g.:
  ```jsonc
  // appsettings.json (committed — empty so real envs must override)
  "AdminSeed": { "Email": "", "Password": "" }
  // appsettings.Development.json (local defaults)
  "AdminSeed": { "Email": "admin@fooddeliveryservice.com", "Password": "admin" }
  ```
  A startup seeder runs only when both values are non-empty: create the `ApplicationUser` in Identity (email confirmed, active — **not** invited) with that password, and the matching `User` with the `Administrator` role in Users. Idempotent (skip if the admin already exists). Because production `appsettings.json` is empty, the seeder no-ops there and the admin must be provisioned via a real secret (Key Vault / env var) — never the committed default. The dev password (`admin`) is intentionally weak; ASP.NET Identity password-strength rules may need relaxing in Development for it to seed.
- **Gateway:** add an **anonymous** `users/accept-invitation` route (copy the `users/register` route shape), ordered before the `users/{**catch-all}` route. Keep `users/register` anonymous (customers). No anonymous `restaurants/register` route — onboarding is authenticated under the existing `restaurants/{**catch-all}`.
- **Notifications:** register the `UserInvitedIntegrationEvent` consumer; add the handler + `IEmailService`. This is the first real consumer in the Notifications module.
- **Observability:** Restaurants/Notifications hosts already inherit `AddInfrastructure` (OTel + Serilog). The new email send is an external call — instrument the `IEmailService` (an OTel activity/span) per the "instrument new external calls" rule.
- **Validation:** FluentValidation for every command (non-empty name, positive price, valid cuisine, well-formed address, valid email, and `CommissionRate` in range `0 ≤ rate < 1`).

---

## 8. Suggested build order

Sequenced as three milestones, each independently buildable and verifiable. **Milestone A (the Administrator) comes first** and must be fully working before any restaurant work — it's the foundation every later step relies on (only an admin can onboard restaurants), and standing it up first de-risks the whole flow: it proves identity seeding, role/permission seeding, and end-to-end authorization work before onboarding logic is layered on top.

### Milestone A — Administrator foundation (do this first, verify before proceeding)
1. **Users — roles & permissions:** rename the `Member` role → `Customer`; add the `RestaurantManager` role + restaurant/menu/`users:provision` permissions; assign create/provision rights to `Administrator` and manage rights to `RestaurantManager` in `PermissionConfiguration`; update `RoleConfiguration`; migration. (Only the `Administrator` assignments are exercised in this milestone; the `RestaurantManager` ones are seeded now but used later.)
2. **Identity — admin seeder:** add the config-driven `AdminSeed` options (empty in `appsettings.json`, `admin@fooddeliveryservice.com` / `admin` in `appsettings.Development.json`); startup seeder creates the active `ApplicationUser` (email confirmed, **not** invited) when both values are non-empty, idempotently. Relax Development password rules so the weak dev password seeds.
3. **Users — admin record:** seed the matching `User` with the `Administrator` role (aligned to the Identity admin's `IdentityId`), so permission resolution returns the admin's permissions.
4. **Verify Milestone A:** the seeded admin authenticates against Identity (public client) and obtains a JWT; a protected probe (any endpoint requiring an admin permission, or a temporary `GET` guarded by `restaurants:read`) returns **200** for the admin, **401** anonymously; `GetUserPermissionsRequest` resolves the admin's permission set (cached in Redis). Confirm the seeder no-ops when `AdminSeed` is empty.

### Milestone B — Invitation & provisioning plumbing
5. **Identity — invited provisioning:** add the `MustChangePassword` flag; add invite-create (no password + one-time token) and token-consume (set password) to the local API.
6. **Users — create-with-role + invitation:** extend `RegisterUserCommand`/handler/validator, `User.Create`, and `IIdentityProviderService`; add `UserInvitedIntegrationEvent` + roles on `UserRegisteredIntegrationEvent`; add `POST users/register` (customer) and `POST users/accept-invitation`.
7. **Users — RPC consumer:** `ProvisionManagerUserRequest`/`Response` + `ProvisionManagerUserRequestConsumer`; register in `UsersModule.ConfigureConsumers`.
8. **Notifications — invitation email:** register the `UserInvitedIntegrationEvent` consumer; add the handler + `IEmailService` (log/SMTP for dev).
   - *Verify Milestone B (without Restaurants):* invoke `ProvisionManagerUserRequest` directly (or via a temporary harness) → an invited account + `UserInvitedIntegrationEvent` → email logged → `accept-invitation` activates the account and the user can log in.

### Milestone C — Restaurants & menu
9. **Restaurants — domain:** `Restaurant` (with `ManagerUserId`), `MenuCategory`, `MenuItem`, `Address`, `RestaurantManager`, errors, domain events (replace the stub).
10. **Restaurants — persistence:** DbSets + EF configs + migration `Add_Restaurants_And_Menu`; repositories.
11. **Restaurants — integration handlers:** `UserRegistered`/`UserProfileUpdated` → manager replica.
12. **Restaurants — onboarding:** `OnboardRestaurantCommand` (RPC to Users → create `Restaurant` in one UoW) + `POST restaurants` (Administrator) + compensation/reconciliation for partial failure.
13. **Restaurants — menu & profile:** update restaurant, menu categories/items, availability, get menu/restaurant (Dapper).
14. **Restaurants — publish** `RestaurantRegisteredIntegrationEvent`.
15. **Verify** the full flow (§9).

---

## 9. Verification / definition of done

- `dotnet build` clean; all new migrations apply on startup; the seeded Administrator (`admin@fooddeliveryservice.com` / `admin` in Development) can log in; the seeder no-ops when `AdminSeed` values are empty (production).
- **Admin onboarding:** an Administrator token calls `POST restaurants` with a commission rate → the `Restaurant` is persisted (the admin-supplied `CommissionRate`, `Status=Active`, `ManagerUserId` set) **and** an invited `RestaurantManager` account is created (no usable password yet). A duplicate manager email surfaces as a clean failure, not a 500; an out-of-range commission is rejected by validation.
- **Invitation:** a `UserInvitedIntegrationEvent` is consumed by Notifications and an invitation email with an activation link is produced (logged in dev). The invited account cannot log in until activated. The manager opens the link and sets a password via `POST users/accept-invitation`, then logs in. An expired/invalid token is rejected.
- **Replica:** shortly after onboarding, the `RestaurantManager` replica row appears in the Restaurants DB (async event → inbox); the `Restaurant` did not block on it.
- **Menu:** the activated manager creates categories + items; `GET restaurants/{id}/menu` returns them; toggling availability works.
- **Authorization:** a `RestaurantManager` token is **rejected (403)** on `POST restaurants` (cannot onboard/provision); a `Customer` token is rejected on all restaurant writes; a second manager cannot modify the first manager's restaurant (`NotManager`); an Administrator can.
- **Partial-failure path:** simulate a `Restaurant` save failure after a successful RPC → the compensation/reconciliation path removes (or flags) the orphaned invited account.
- No hard-rule violations: reads use Dapper; cross-service contact only via the bus (RPC + events); domain logic lives in the entities; `UserRegisteredIntegrationEvent` stays identity/role-only.

---

## 10. Open questions for later phases (not this iteration)

- Approval/verification workflow (documents, food-safety, bank details) — the contract is handled off-platform; skipped by design.
- Operating hours & delivery zones — needed before ordering goes live; `Address` lat/long is scaffolded for it.
- Menu **modifiers** (toppings/sides/portions) — deferred.
- Photo upload / professional-photography pipeline — only a `PhotoUrl` is stored for now.
- POS / order-tablet integration — a later, ordering-phase concern.
- Invitation lifecycle polish — resend invite, revoke/expire, admin UI to see pending invitations.

**Actor-model note (platform-wide).** This admin-provisioning + invitation flow is the template for every non-customer actor. **Users** owns identity + role for all of them (a single account can hold multiple roles). Only **Customers** (`Customer`) self-register via `users/register`. **RestaurantManagers**, and later **DeliveryDrivers** (Delivery service) and **SupportAgents** (CustomerSupport service), are created by an **Administrator** and activated by email invitation — each via a `Provision…UserRequest` RPC (or a shared one with a role parameter) and a domain-owned profile aggregate keyed by `UserId`, built from `UserRegisteredIntegrationEvent`. Identity (Duende) and Users stay separate — protocol-level auth vs. cross-cutting role/permission registry.
