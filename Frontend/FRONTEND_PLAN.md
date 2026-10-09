# 🍕 Food Delivery Platform — Angular Frontend Implementation Plan

> A responsive Angular web application for the FoodDeliveryService backend. It serves all five user
> types — **Customers**, **Restaurant Managers**, **Delivery Drivers**, **Support Agents**, and
> **Administrators** — from a single codebase. Customers and drivers will use it almost exclusively
> on phones, so the app is **mobile-first by design**.
>
> **Who this plan is for:** a .NET backend developer who just finished a solid Angular course and is
> building their first real frontend. Every milestone explains *what* to build, *why* it's built
> that way, and *which frontend concept it teaches*. Backend remains the main skill — this project
> proves competent, modern, employable Angular knowledge without chasing "high-tech" frontend
> architecture.
>
> **Revised 2026-09-19 against the backend that exists, not the one that was planned.** The first
> version of this document was written on 2026-07-19 from `FoodDelivery_ProjectPlan.md` — the *plan*
> for the backend, not the backend. Since then Support (3.6), Production Hardening (3.7) and Card
> Payments (3.8) shipped; Fraud Detection (3.4) was built and then reverted; the Kubernetes feature
> was scoped down; and several things this plan assumed — reviews, restaurant search, the three AI
> features, a *writable* user-profile endpoint — were never built at all. Every endpoint, permission,
> DTO field and hub method named below was read out of the code on that date. Where a screen needs
> something that does not exist, it is listed as a **backend prerequisite with its shape**, never
> designed around.
>
> **Aligned 2026-10-09 with the UI design** — the 61-sheet canvas *Food Delivery Platform UI*
> (see [UI design reference](#ui-design-reference)). Every milestone now names the sheets it builds.
> The alignment ran both ways: canvas boards that contradicted the contract were corrected, and the
> UX decisions taken during design were written back into this plan. The design also surfaced five
> new backend gaps, added below as prerequisites **#5–#9**.

---

## Table of Contents

- [Part 1 — High-Level Overview](#part-1--high-level-overview)
  - [What we are building](#what-we-are-building)
  - [Technology stack](#technology-stack)
  - [Architecture](#architecture)
  - [Project structure](#project-structure)
  - [How the frontend talks to the backend](#how-the-frontend-talks-to-the-backend)
  - [The API surface as it exists today](#the-api-surface-as-it-exists-today)
  - [Roles, permissions, and which portal may call what](#roles-permissions-and-which-portal-may-call-what)
  - [The two order-status vocabularies](#the-two-order-status-vocabularies)
  - [Backend prerequisites](#backend-prerequisites)
  - [Coverage matrix — every backend feature and its real state](#coverage-matrix--every-backend-feature-and-its-real-state)
  - [What this plan deliberately does not build](#what-this-plan-deliberately-does-not-build)
  - [UI design reference](#ui-design-reference)
- [Part 2 — Detailed Implementation Plan](#part-2--detailed-implementation-plan)
  - [Phase 0 — Foundations](#phase-0--foundations)
  - [Phase 1 — Core Features](#phase-1--core-features)
  - [Phase 2 — Real-Time & Driver Experience](#phase-2--real-time--driver-experience)
  - [Phase 3 — Payments, Support & Production Polish](#phase-3--payments-support--production-polish)
- [Conventions & Working Rules](#conventions--working-rules)
- [Testing Strategy](#testing-strategy)
- [Learning with AI Tools](#learning-with-ai-tools)

---

# Part 1 — High-Level Overview

## What we are building

**One single-page application (SPA)** with five role-based areas, not five separate apps:

| Area | Users | Primary device | Key screens |
|---|---|---|---|
| **Customer** | Customers | 📱 Phone | Browse restaurants, menu, cart, checkout (cash or saved card), live order tracking with the driver on a map, order history, saved cards, my support tickets |
| **Restaurant** | Restaurant Managers | 💻 Tablet/desktop | Live incoming-orders dashboard, accept/reject, preparation flow, menu & category management, sold-out toggle, restaurant profile |
| **Driver** | Delivery Drivers | 📱 Phone (always) | Go online/offline, offer inbox with countdown, accept/reject, navigate to pickup, mark picked-up/delivered, delivery history |
| **Admin** | Administrators | 💻 Desktop | Onboard restaurants (+ their manager), onboard drivers, decide refund requests, platform overview |
| **Support** | Support Agents | 💻 Desktop | Ticket queue, ticket detail with the customer thread + internal notes, audit trail, refund requests, analytics summary |

**Why one app instead of several?** One app is simpler to build, test, and deploy; role-based lazy
loading means a driver's phone never downloads the admin screens; and it demonstrates the
job-relevant skills (route guards, lazy loading, role-based UI) better than duplicating
boilerplate across repos. Real companies with separate apps still structure each one exactly like
one of our role areas — so nothing is lost for learning.

**Mobile-first is non-negotiable.** Every customer and driver screen is designed for a ~375 px
wide phone screen first, then progressively enhanced for tablet/desktop with Tailwind's responsive
prefixes (`sm:`, `md:`, `lg:`). Restaurant/admin/support screens are desktop-first but must remain
usable on a tablet.

> ⚠️ **There is no anonymous browsing.** `GET restaurants` and `GET restaurants/{restaurantId}/menu`
> each carry a permission (`restaurants:read`, `menu:read`) that only an authenticated user holds.
> Exactly three routes on the whole platform are anonymous: `POST users/register`,
> `POST users/accept-invitation` and `POST payments/webhooks/stripe` (Stripe's, not yours). Every
> screen except login, registration and invitation activation therefore sits behind a token — there
> is no public storefront to build, and no SEO story to tell. Say that in the README; it is the
> reason SSR is skipped.

---

## Technology stack

Chosen for two criteria: **most common in job postings** and **learnable by a beginner**. Nothing
exotic.

| Technology | Category | Why |
|---|---|---|
| **Angular (latest stable)** | Framework | Standalone components, signals, built-in router/HTTP/forms. The batteries-included framework — closest in spirit to ASP.NET Core, and heavily used in enterprises that also run .NET backends (your target employers). |
| **TypeScript (strict mode)** | Language | Angular's language. Strict mode catches the same class of bugs the C# compiler catches — lean on it. |
| **Tailwind CSS v4** | Styling | Utility-first CSS. No context-switching to separate stylesheet files; responsive design via `sm:`/`md:` prefixes; the most in-demand styling approach in current job postings. |
| **Angular Signals** | State management | Angular's built-in reactive primitive. Component and service state as `signal()` / `computed()`. Simpler than RxJS-everywhere or NgRx, and it is the direction the framework itself is going. |
| **RxJS (targeted use)** | Async streams | Only where it genuinely fits: HTTP calls, the debounced filters on the support queue, SignalR event streams, polling. Knowing `map`, `switchMap`, `debounceTime`, `catchError` covers this project. |
| **Angular Reactive Forms (typed)** | Forms | Login, registration, checkout, menu editing, category reordering, ticket replies, refund decisions. The typed-forms API is the standard answer to "how do you handle forms in Angular?" in interviews. |
| **@microsoft/signalr** | Real-time | Official SignalR JavaScript client — connects to `hubs/tracking` **through the gateway** for live order status, driver location and driver offers. |
| **Leaflet + OpenStreetMap** | Maps | Free, no API key, tiny learning curve. Displays the driver's live position for customers and the pickup/drop-off pins for drivers. (Google Maps needs billing setup; Leaflet is the standard free choice.) |
| **@stripe/stripe-js** | Payments | Stripe Elements collects the card **in the browser, against Stripe directly**. The backend's entire PCI posture (SAQ-A) rests on the card never reaching this SPA's own API calls. See Milestone 3.1. |
| **angular-eslint + Prettier** | Code quality | Linting and formatting exactly like `dotnet format` + analyzers. Set up once, forget. |
| **Vitest** | Unit tests | Angular's current default test runner (replaces Karma). Fast, simple API. |
| **Playwright (small suite)** | E2E tests | 3–5 happy-path browser tests. Even a tiny E2E suite is a strong CV signal. |
| **GitHub Actions** | CI/CD | Build + lint + test on every push. `.github/workflows/ci.yml` already exists for the backend — add a `Frontend/**` job to it rather than starting a second workflow. |

**Deliberately NOT used (and why):**

- **NgRx Store** — the classic Redux-style state library. Very common in legacy enterprise job
  postings, but overkill for this app and a steep learning curve. Signals in services cover our
  needs. *Optional stretch goal:* refactor ONE feature (the cart) to `@ngrx/signals` SignalStore
  at the end, so you can honestly discuss it in interviews.
- **Angular Material / PrimeNG** — component libraries would fight Tailwind and hide the CSS
  learning. We build a small set of our own UI components instead (great learning, great
  portfolio evidence). If a screen needs something genuinely hard (date picker), reconsider then.
- **Server-side rendering (Angular SSR)** — meaningful for SEO/marketing pages; this app is behind
  a login end to end. Skip.
- **Micro frontends, module federation, monorepo tooling (Nx)** — real technologies, wrong
  project size. Mentioning *why* you didn't use them is itself a good interview answer.
- **An Application Insights browser SDK** — the backend has no Application Insights. Its telemetry
  stack is OpenTelemetry Collector → Prometheus/Grafana, plus Jaeger and Seq (backend Feature 2.4
  Milestone E); the optional Azure Monitor milestone was never built, and a grep for
  `ApplicationInsights` across the solution returns nothing. The honest browser-side equivalent is
  in Milestone 3.4, and it is smaller and better: correlate on the `X-Correlation-Id` the gateway
  already exposes to the browser by name.

---

## Architecture

### The big picture

```
┌─────────────────────────────── Angular SPA ───────────────────────────────┐
│                                                                           │
│  features/customer/**   features/restaurant/**   features/driver/**  ...  │
│        (lazy)                  (lazy)                  (lazy)             │
│           │                       │                       │               │
│           └───────────┬───────────┴───────────┬───────────┘               │
│                       ▼                       ▼                           │
│              core/  (auth, interceptors, guards, api services)            │
│              shared/ (UI components, pipes, directives)                   │
└───────┬───────────────────────────────────────────┬───────────────────────┘
        │ REST (JSON) + WebSocket                   │ tokens
        ▼                                           ▼
  YARP Gateway :3000                          Identity :18080
    users/**  orders/**  restaurants/**        POST /connect/token
    delivery/**  payments/**  support/**       (login + refresh)
    hubs/**  ← SignalR, same origin
    docs/{slug}/**  ← Scalar + Swagger
```

The SPA holds **two** base URLs, not three. The realtime service does listen on `:5600` in
`docker-compose`, but the browser never uses that port: the gateway has a route
`fooddeliveryservice-realtime-route1` matching `hubs/{**catch-all}` that forwards the WebSocket
upgrade to it. Talking to `:5600` directly would mean a third origin to CORS-allow, a second place
to configure, and a deployment story that only works on a laptop.

### Key architectural decisions

1. **Standalone components everywhere** (no NgModules). This is the modern Angular default and
   what every current course teaches.

2. **Feature-based folder structure with lazy loading.** Each role area is a folder of routes
   loaded with `loadChildren` only when a user of that role navigates there. This mirrors how the
   backend splits modules — one bounded context per folder.

3. **Smart/dumb component split (lightweight version).** Page components ("smart") inject
   services and own data; presentational components ("dumb") receive data via `input()` and emit
   via `output()`. Don't be religious about it — just keep API calls out of leaf components.

4. **State lives in services holding signals.** A `CartService` holds `items = signal<CartItem[]>([])`
   plus `computed()` totals. Components read signals directly in templates. This is the modern,
   simple, interview-defensible pattern: *"signal-based services; I'd reach for NgRx if state
   became complex or needed devtools/time-travel."*

5. **One typed API client service per backend module** (`OrdersApi`, `RestaurantsApi`,
   `DeliveryApi`, `PaymentsApi`, `SupportApi`, `UsersApi`), each a thin wrapper over `HttpClient`
   returning typed DTOs that mirror the backend contracts. All backend URLs come from
   `environment.ts` — never hard-coded in components.

6. **Errors follow the backend's `ProblemDetails` format, and `title` is a machine code.** Every
   failed `Result` comes back through one helper (`Common.Presentation/Results/ApiResults.cs`) that
   maps the domain error to RFC 7807 problem+json with:
   - `title` = the error **code**, e.g. `Orders.PaymentNotAuthorized`, `Restaurants.NotFound`
   - `detail` = the human sentence, e.g. *"The order cannot be accepted until its payment has been
     authorized"*
   - `status` = 400 for `Validation` and `Problem`, **404** for `NotFound`, **409** for `Conflict`
   - `errors` (an extension member) = the field errors, present only on a `ValidationError`

   So your interceptor should branch on `title` and *display* `detail`. Parsing `detail` for
   meaning is the beginner mistake here; it is prose and it will change.

### What every JSON response looks like

Two facts that will bite on the very first API call if you don't know them, both verified rather
than assumed — **nowhere in the solution is a `JsonStringEnumConverter` or a custom
`JsonSerializerOptions` registered**, so minimal APIs run on `JsonSerializerDefaults.Web`:

- **Property names are camelCase.** `OrderResponse.PlacedOnUtc` arrives as `placedOnUtc`.
- **Enums are numbers, in *responses*.** `OrderResponse.Status` arrives as `1`, not `"Pending"`.
  Same for `paymentStatus`, `paymentMethod`, `status` on a delivery, ticket, refund and everything
  else.
- **But enums are *names* in requests.** `PlaceOrder.PaymentMethod`, `OnboardDriver.VehicleType`,
  `ChangeTicketStatus.Status`, `PostTicketMessage.Visibility` and `GetTickets`' `status`/`category`
  query parameters are all declared as `string` and parsed from the enum member name.

That asymmetry — numbers out, names in — is not a bug to work around, it is the contract. Model it
once, in `core/api/models/`, as a numeric TypeScript enum per backend enum plus a name constant for
the request side, and never think about it again:

```ts
export enum OrderStatus { Pending = 1, Accepted = 2, Rejected = 3, Preparing = 4,
                          ReadyForPickup = 5, OutForDelivery = 6, Delivered = 7, Cancelled = 8 }

export const PaymentMethodName = { CashOnDelivery: 'CashOnDelivery', Card: 'Card' } as const;
```

Confirm it yourself in thirty seconds before you write a line: open
`http://localhost:3000/docs/orders/scalar`, run `GET orders/{id}` and look at the raw response.

### Authentication design (matches the existing backend exactly)

The backend's Duende IdentityServer has a **public client with the resource-owner-password grant
and refresh tokens enabled** (`fooddeliveryservice-public-client`, `Identity/Config.cs`). The
frontend does **not** need an OIDC redirect library — login is a plain form POST:

```
POST http://localhost:18080/connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=password
client_id=fooddeliveryservice-public-client
username={email}
password={password}
scope=openid profile email fooddeliveryservice.api offline_access
```

Response: `access_token` (JWT) + `refresh_token`. Refresh uses the same endpoint with
`grant_type=refresh_token`.

Token lifetimes, from `Config.cs`, so you know what you are testing against:

| Setting | Value | Why it matters to you |
|---|---|---|
| `AccessTokenLifetime` | **15 minutes** | You will see refresh happen in a normal session. Good. |
| `RefreshTokenUsage` | **OneTimeOnly** | Using a refresh token twice invalidates the whole chain. Two concurrent refreshes will log the user out — this is why single-flight refresh matters here more than in most apps. |
| `SlidingRefreshTokenLifetime` | 8 hours idle | |
| `AbsoluteRefreshTokenLifetime` | 7 days | |
| Account lockout | 5 failed attempts → 15 min | Your login form's error message should say so, and a dev account you fat-finger five times really will lock. |

Frontend responsibilities:

- **`AuthService`** — logs in, stores tokens, fetches `GET users/me` for the identity the token does
  not carry, exposes `currentUser` / `isLoggedIn` / `roles` as signals, schedules/executes refresh,
  logs out.
- **Auth interceptor** — attaches `Authorization: Bearer …` to every gateway request; on a 401,
  attempts one token refresh and replays the request; if refresh fails, redirects to login.
- **Route guards** — `authGuard` (must be logged in) and `roleGuard('Customer')` etc. per area.
  After login, the app routes to the user's home area based on their role(s).
- **Token storage:** `localStorage`, with an honest README note about the XSS trade-off and what
  production would do differently (BFF/cookie pattern).

> 🚧 **The access token does not tell you who the user is — `GET users/me` does.** The token half of
> that is still worth understanding before you write a line of `AuthService`.
> `CustomClaimsTransformation` (`Common.Infrastructure/Authorization/`) adds the `sub` (module-side
> user id) and `permission` claims **server-side, on every request, after JWT validation** — they
> are never minted into the token. And Identity assigns no ASP.NET Identity roles at all: roles live
> only in the Users module's database. The `ApiResource("fooddeliveryservice.api")` declares no
> `UserClaims` either, so the access token's user-identifying content is essentially just `sub` —
> and that `sub` is the **Duende identity id**, a different value from the module-side user id that
> `OrderResponse.customerId` and the SignalR groups use.
>
> So you make one extra call after login: `GET users/me` returns the module-side `userId`, the name,
> the email and the `roles`, none of which the token carries. **Two ids, and they are never
> interchangeable** — `sub` identifies the account at the identity provider, `userId` is what every
> other service's DTOs and hub groups are keyed by. Model them as separate fields with separate
> names and the trap never fires.
>
> Decode a real token at jwt.io on day one and see for yourself — seeing how little is in there is
> what makes the `/me` call make sense. And keep this straight: what `/me` gives you drives
> navigation and rendering. It is **not** an authorization check. Every service still resolves
> permissions server-side per request, so a client that lies to itself about its role gets prettier
> menus and exactly the same 403s.

> ⚠️ ROPC (password grant) is deprecated in OAuth 2.1 — the backend chose it deliberately for
> simplicity. Be ready to say in interviews: "production would use Authorization Code + PKCE with
> redirect to the identity provider; my backend exposes ROPC so the SPA posts credentials
> directly." Knowing *why* is worth more than the fancier flow.

---

## Project structure

The frontend lives in `Frontend/` next to `Backend/` in the same monorepo.

```
Frontend/
├── FRONTEND_PLAN.md                  # this file
└── food-delivery-web/                # ng new output
    ├── src/
    │   ├── app/
    │   │   ├── core/                 # singletons — "the plumbing"
    │   │   │   ├── auth/             #   AuthService, token storage, guards, user model
    │   │   │   ├── api/              #   one API client per backend module + shared DTO types
    │   │   │   ├── interceptors/     #   authInterceptor, errorInterceptor
    │   │   │   ├── realtime/         #   SignalR connection service (Phase 2)
    │   │   │   └── layout/           #   app shell: header, mobile bottom nav, sidebar
    │   │   ├── shared/               # reusable, stateless building blocks
    │   │   │   ├── ui/               #   button, input, card, badge, spinner, modal, toast,
    │   │   │   │                     #   empty-state, chat-bubble, bar-chart, map
    │   │   │   └── pipes/            #   money pipe, relative-time pipe, enum-label pipes
    │   │   ├── features/
    │   │   │   ├── auth/             #   login, register, activate-invitation pages
    │   │   │   ├── customer/         #   restaurants, menu, cart, checkout, orders, tracking,
    │   │   │   │                     #   payment-methods, my-tickets
    │   │   │   ├── restaurant/       #   dashboard, orders, menu-editor, profile
    │   │   │   ├── driver/           #   home (online/offline), offers, active delivery, history
    │   │   │   ├── admin/            #   restaurant onboarding, driver onboarding, refund decisions
    │   │   │   └── support/          #   ticket queue, ticket detail, refunds, analytics
    │   │   ├── app.routes.ts         # top-level routes; each feature lazy-loaded
    │   │   ├── app.config.ts         # providers: router, http+interceptors, etc.
    │   │   └── app.component.ts
    │   ├── environments/             # environment.ts / environment.development.ts (API URLs)
    │   ├── styles/tokens.css         # design tokens (plain :root variables, dark theme, density)
    │   └── styles.css                # imports tokens.css, then Tailwind; @theme inline bridge
    ├── eslint.config.js
    └── package.json
```

**Rule of thumb:** `core/` = injected once, app-wide. `shared/` = imported everywhere, no
business logic, no HTTP. `features/` = pages and feature-specific components; may only depend on
`core/` and `shared/`, never on another feature.

---

## How the frontend talks to the backend

| Backend service | URL (local dev) | Frontend uses it for |
|---|---|---|
| **YARP Gateway** | `http://localhost:3000` | Everything the browser does over HTTP **and** the WebSocket: `users/**`, `restaurants/**`, `orders/**`, `delivery/**`, `payments/**`, `support/**`, and `hubs/**` |
| **Identity (Duende)** | `http://localhost:18080` | `POST /connect/token` — login and refresh, and nothing else |

**Two** base URLs in `environment.ts`. The individual services (Orders :5200, RealTime :5600,
Payments :5800 …) are an internal backend detail the frontend never sees — exactly the point of the
gateway. Token issuance is the one exception: it does not pass through the gateway, by design
(`docs/security.md` §6.3), which is why Identity gets its own entry and its own CORS prerequisite.

Two more gateway facts worth knowing up front:

- **`notifications/**` is routed but empty.** The gateway proxies it, and the Notifications service
  has never exposed a single HTTP endpoint — it is a pure event consumer that sends email. There is
  no notifications API to call. See Milestone 2.1 step 5.
- **The docs are live and proxied.** `http://localhost:3000/docs/{orders|restaurants|users|delivery|support|payments|realtime|notifications}`
  redirects to Scalar; `/docs/{slug}/swagger` is the same document in Swagger UI, and
  `/docs/{slug}/openapi/v1.json` is the raw document. Three of those are empty on purpose
  (`notifications` has no endpoints, `realtime` only has a hub and `MapHub` contributes no
  `ApiDescription`, and `payments`' document predates its endpoints). Hand-write your DTOs from the
  other five.

---

## The API surface as it exists today

Read this table before designing any screen. Everything here was read out of the `IEndpoint`
classes — the bulk of it on 2026-09-19, the Users rows re-checked on 2026-09-24; anything not in it
does not exist.

### Users — `users/**` (4 endpoints, the two registration ones anonymous)

| Method & path | Permission | Body / query | Returns |
|---|---|---|---|
| `POST users/register` | *anonymous* | `{ email, password, firstName, lastName }` | `Guid` (module user id). Role is **forced to Customer** server-side whatever you send |
| `POST users/accept-invitation` | *anonymous* | `{ email, token, newPassword }` | `204` |
| `GET users/me` | `users:read` | *none — the subject is the token* | `{ userId, email, firstName, lastName, roles: string[] }`. `404` if the account was deactivated since the token was minted |
| `PUT users/{userId}/roles` | `user-roles:manage` | `{ roles: string[] }` | `204` |

That is the entire Users HTTP surface. **There is no `users/profile`, no user list, no provisioning
endpoint.** Provisioning happens over the message bus
(`ProvisionUserRequest` / `ProvisionManagerUserRequest`), triggered by the restaurant and driver
onboarding endpoints below.

`GET users/me` is the one endpoint the SPA cannot do without: `roles` and the module-side `userId`
exist nowhere in the access token (see the [authentication
design](#authentication-design-matches-the-existing-backend-exactly)). It takes no parameter, so
there is no way to ask it about anybody else. `users:read` is held by **every** role, so the
permission is not what decides whose record comes back — the handler is, from the JWT subject.

### Restaurants — `restaurants/**`

| Method & path | Permission | Body / query | Returns |
|---|---|---|---|
| `GET restaurants` | `restaurants:read` | **`page`, `pageSize` only** (`pageSize` 1–100) | `RestaurantResponse[]` |
| `GET restaurants/{id}` | `restaurants:read` | — | `RestaurantResponse` |
| `POST restaurants` | `restaurants:create` (admin) | restaurant fields + `commissionRate` + `managerEmail/FirstName/LastName` | `Guid` |
| `PUT restaurants/{id}` | `restaurants:update` | name, taxIdentification, cuisineType, email, phone, address, lat/lng | `204` |
| `GET restaurants/{restaurantId}/menu` | `menu:read` | — | `MenuResponse` |
| `POST restaurants/{restaurantId}/menu-categories` | `menu:manage` | `{ name, displayOrder }` | `Guid` |
| `PUT restaurants/{restaurantId}/menu-categories/{categoryId}` | `menu:manage` | `{ name, displayOrder }` | `204` |
| `POST restaurants/{restaurantId}/menu-items` | `menu:manage` | `{ categoryId, name, description, price, photoUrl?, isAvailable }` | `Guid` |
| `PUT restaurants/{restaurantId}/menu-items/{menuItemId}` | `menu:manage` | `{ name, description, price, photoUrl? }` | `204` |
| `PATCH restaurants/{restaurantId}/menu-items/{menuItemId}/availability` | `menu:manage` | `{ isAvailable }` | `204` |

`RestaurantResponse` = `id, managerUserId, name, taxIdentification, cuisineType, email, phoneNumber,
street, city, postalCode, country, latitude?, longitude?, commissionRate, status, createdOnUtc`.
**No opening hours, no rating, no review count, no logo, no distance.**
`commissionRate` is a **fraction in [0, 1)** — `0.20` means 20%.
`status` is `Active = 1 | Suspended = 2`, and nothing in the API can change it.

`MenuResponse` = `{ restaurantId, categories: [{ id, name, displayOrder, items: [{ id, name,
description, price, photoUrl?, isAvailable }] }] }`.

**There is no DELETE anywhere in this module** — no way to remove a category or an item. Marking an
item unavailable is the closest thing.

### Orders — `orders/**`

| Method & path | Permission | Body / query | Returns |
|---|---|---|---|
| `POST orders` | `orders:create` | `{ restaurantId, items: [{ menuItemId, quantity }], deliveryAddress: { street, city, postalCode, country, notes?, latitude?, longitude? }, paymentMethod: "CashOnDelivery" \| "Card" }` + **`Idempotency-Key` header** | `Guid` |
| `GET orders` | `orders:read` | `page`, `pageSize` | `OrderSummaryResponse[]` — **self-scoped** |
| `GET orders/{id}` | `orders:read` | — | `OrderResponse` |
| `POST orders/{id}/cancel` | `orders:create` (the *customer's* code) | — | `204` |
| `POST orders/{id}/accept` | `orders:manage` | — | `204` |
| `POST orders/{id}/reject` | `orders:manage` | `{ reason }` | `204` |
| `POST orders/{id}/preparing` | `orders:manage` | — | `204` |
| `POST orders/{id}/ready` | `orders:manage` | — | `204` |

The request carries **menu item ids and quantities, never prices** — the server prices every line
from its own menu replica.

`OrderResponse` = `id, customerId, restaurantId, status, paymentMethod, paymentStatus, subtotal,
commissionRate, street, city, postalCode, country, notes?, placedOnUtc, items: [{ id, menuItemId,
name, unitPrice, quantity, lineTotal }]`.

**An order has a `subtotal` and a `commissionRate` and nothing else.** There is no delivery fee, no
tax, no tip and no grand total — `commissionRate` is the platform's cut of the subtotal, a
business-side number, not something to add to what the customer pays. Any screen that renders
"Total" is inventing a number.

`GET orders` is scoped in the handler from the authenticated identity — a customer's own, a
manager's incoming (by owned restaurant), all of them for an administrator. **There is no parameter
that widens it**, and no parameter that narrows it either: no status filter, no restaurant filter,
no date range. Just `page` and `pageSize`.

### Delivery — `delivery/**`

| Method & path | Permission | Body / query | Returns |
|---|---|---|---|
| `POST delivery/drivers` | `users:provision` (admin) | `{ email, firstName, lastName, vehicleType: "Bicycle" \| "Motorcycle" \| "Car" }` | `Guid` (= the provisioned user id) |
| `GET delivery/drivers/me` | `drivers:read` | — | `DriverResponse` |
| `PUT delivery/drivers/me` | `drivers:update` | `{ firstName, lastName, vehicleType }` | `204` |
| `PATCH delivery/drivers/me/availability` | `drivers:update` | `{ available: boolean }` | `204` |
| `POST delivery/drivers/me/location` | `drivers:update` | `{ latitude, longitude }` | `204` |
| `GET delivery/drivers/me/offers` | `drivers:read` | — | `DeliveryOfferResponse[]` |
| `GET delivery/drivers/{id}` | `drivers:read` | — | `DriverResponse` |
| `GET delivery/deliveries` | `deliveries:read` | `page`, `pageSize` | `DeliverySummaryResponse[]` — **self-scoped** |
| `GET delivery/deliveries/{id}` | `deliveries:read` | — | `DeliveryResponse` |
| `GET delivery/orders/{orderId}/delivery` | `deliveries:read` | — | `DeliveryResponse` |
| `POST delivery/deliveries/{id}/accept` | `deliveries:manage` | — | `204` (`409` if you lost the race) |
| `POST delivery/deliveries/{id}/reject` | `deliveries:manage` | — | `204` |
| `POST delivery/deliveries/{id}/picked-up` | `deliveries:manage` | — | `204` |
| `POST delivery/deliveries/{id}/delivered` | `deliveries:manage` | — | `204` |

`DeliveryOfferResponse` = `id, orderId, restaurantId, pickupLatitude, pickupLongitude,
dropoffStreet, dropoffCity, dropoffPostalCode, dropoffCountry, dropoffNotes?, dropoffLatitude,
dropoffLongitude, offerExpiresOnUtc, createdOnUtc` — soonest deadline first, lapsed offers excluded
server-side. **No driver name, no live position, no status: every row is `Offered` by
construction.**

`DeliveryResponse` adds the driver (`driverId?`, `driverFirstName?`, `driverLastName?`), the
timestamps (`offerExpiresOnUtc?`, `assignedOnUtc?`, `pickedUpOnUtc?`, `deliveredOnUtc?`) and the
live position (`currentDriverLatitude?`, `currentDriverLongitude?`,
`currentDriverLocationRecordedOnUtc?`, read from Redis, null once terminal or stale).

`DeliveryStatus` = `Pending = 0, Offered = 1, Assigned = 2, PickedUp = 3, Delivered = 4,
Unassigned = 5, Cancelled = 6`. `DriverStatus` = `Offline = 1, Available = 2, Busy = 3`.
`VehicleType` = `Bicycle = 1, Motorcycle = 2, Car = 3`.

Like `GET orders`, **`GET delivery/deliveries` is scoped from the token and has no widening
parameter** — a driver's own history, or all of them for an administrator.

### Payments — `payments/**`

| Method & path | Permission | Body / query | Returns |
|---|---|---|---|
| `GET payments/payment-methods` | `payment-methods:manage` | — | `PaymentMethodResponse[]` (at most one; saving another replaces it) |
| `POST payments/payment-methods/setup-intents` | `payment-methods:manage` | — | `{ setupIntentId, clientSecret }` |
| `DELETE payments/payment-methods/{id}` | `payment-methods:manage` | — | `204` |
| `POST payments/payment-methods/test-cards` | `payment-methods:manage`, **Development only** | `{ stripePaymentMethodId? }` | `Guid` |
| `POST payments/webhooks/stripe` | *anonymous* | Stripe's, not yours | — |

`PaymentMethodResponse` = `id, brand?, last4?, expiryMonth?, expiryYear?, attachedOnUtc?`. That is
everything the platform stores about a card, deliberately.

`payments/payment-methods/test-cards` exists **because this SPA does not yet exist**: it attaches
one of Stripe's server-side test tokens (`pm_card_visa`) so the payment flow can be demonstrated
without a browser card form. Its own doc comment says *"Delete it when the Angular card flow
lands."* Milestone 3.1 is that flow — the test-card endpoint is your scaffolding while you build it,
and removing it is a legitimate backend follow-up you can raise in the same pull request.

Note what is **not** here: `payments:read` is seeded on Customer and Administrator but **no endpoint
uses it**. There is no "my payments" list. An order's payment state is visible in exactly one place,
`OrderResponse.paymentStatus`.

### Support — `support/**`

| Method & path | Permission | Body / query | Returns |
|---|---|---|---|
| `POST support/tickets` | `support-tickets:open` | `{ onBehalfOfCustomerId?, orderId?, subject, category }` | `Guid` |
| `GET support/tickets` | `support-tickets:read` | `status`, `category`, `assignedAgentId`, `unassigned`, `from`, `to`, `page`, `pageSize` | `TicketSummaryResponse[]` |
| `GET support/tickets/{id}` | `support-tickets:read` | — | `TicketResponse` |
| `GET support/tickets/{id}/messages` | `support-tickets:read` | — | `TicketMessageResponse[]` |
| `POST support/tickets/{id}/messages` | `support-tickets:read` | `{ body, visibility: "CustomerVisible" \| "InternalNote" }` | `Guid` |
| `GET support/tickets/{id}/audit` | `support-tickets:manage` (**staff only**) | — | `SupportAuditEntryResponse[]` |
| `POST support/tickets/{id}/status` | `support-tickets:manage` | `{ status, reason? }` | `204` |
| `POST support/tickets/{id}/claim` | `support-tickets:assign` | — | `204` |
| `POST support/tickets/{id}/assign` | `support-tickets:assign` (+ `:administer` to name someone else) | `{ agentId, reason? }` | `204` |
| `POST support/tickets/{id}/unassign` | `support-tickets:assign` | `{ reason }` (required) | `204` |
| `POST support/tickets/{id}/refund-requests` | `refunds:request` | `{ amount, reason }` | `Guid` |
| `GET support/refund-requests` | `refunds:request` | `status`, `page`, `pageSize` | `RefundRequestResponse[]` |
| `POST support/refund-requests/{id}/approve` | `refunds:approve` (**admin only**) | `{ note? }` | `204` |
| `POST support/refund-requests/{id}/reject` | `refunds:approve` (**admin only**) | `{ note? }` | `204` |
| `GET support/analytics/summary` | `support-analytics:read` | `from`, `to` (both default server-side) | `SupportSummaryResponse` |

`GET support/tickets` is **the same endpoint** for the agent queue and a customer's own list —
whose tickets come back is decided from the token. `?status=Open&unassigned=true` is the agent
queue; a customer calling it with the same parameters still only sees their own.

Enums: `TicketStatus` = `Open = 0, InProgress = 1, Resolved = 2, Escalated = 3, Closed = 4`.
`TicketCategory` = `OrderNotReceived = 0, ItemMissing = 1, FoodQuality = 2, DriverIssue = 3,
PaymentIssue = 4, AppIssue = 5, Other = 6`. `TicketPriority` = `Low = 0 … Urgent = 3`.
`TicketSource` = `CustomerPortal = 0, AgentCreated = 1, Chatbot = 2, FraudFlag = 3` — the last two
are reserved enum members with nothing producing them, since neither feature was built.
`TicketAuthorKind` = `Customer = 0, Agent = 1, System = 2`. `TicketMessageVisibility` =
`CustomerVisible = 0, InternalNote = 1`. `RefundStatus` = `Requested = 0, Approved = 1,
Rejected = 2, Settled = 3, Failed = 4`.

### RealTime — the SignalR hub

One hub, at **`hubs/tracking`** through the gateway. `[Authorize]` but **no particular permission** —
customers, drivers, managers and agents all connect to the same hub and each is shown a different
slice.

**The hub has no client→server methods at all.** There is nothing to `invoke`. Group membership is
derived entirely from the connecting principal's claims in `OnConnectedAsync`, and re-derived on
every automatic reconnect:

| Group | Who lands in it |
|---|---|
| `user:{sub}` | **everyone**, where `sub` is the module-side user id |
| `restaurant:{restaurantId}` | a caller holding `restaurants:update` **and** having a RestaurantManager replica row |
| `support` | a caller holding `support:dashboard` |

Server→client methods and their payloads (`TrackingHubMethods` + the frame records):

| Method | Payload | Audience |
|---|---|---|
| `OrderStatusChanged` | `{ orderId, status, occurredOnUtc, driverName?, driverVehicle? }` | the order's customer |
| `DriverLocationChanged` | `{ orderId, driverId, latitude, longitude, recordedOnUtc }` | the order's customer |
| `DeliveryOffered` | `{ deliveryId, orderId, offerExpiresOnUtc }` | the offered **driver** |
| `RestaurantActivity` | `{ orderId, status, occurredOnUtc }` | the restaurant's manager |
| `SupportActivity` | `{ orderId, restaurantId, status, occurredOnUtc }` | all support agents |

`status` on all three status-bearing frames is a **string** from `OrderStatuses`, not a number and
not the REST enum — see the next section.

The socket is explicitly **best-effort**: the hub's own doc comment says the client re-fetches
authoritative state from `GET orders/{id}` and `GET delivery/orders/{orderId}/delivery` on connect
and on every reconnect, then applies socket deltas. Build that re-sync; it is the load-bearing
assumption the backend made when it decided not to persist frames.

---

## Roles, permissions, and which portal may call what

Straight from the seed in `Users.Infrastructure/Users/PermissionConfiguration.cs`. Five roles;
`Administrator` cannot be registered or provisioned — the first one is seeded from configuration.

| Permission | Customer | RestaurantManager | DeliveryDriver | SupportAgent | Administrator |
|---|:--:|:--:|:--:|:--:|:--:|
| `users:read` / `users:update` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `carts:read` / `carts:add` / `carts:remove` | ✅ | | | | ✅ |
| `orders:create` | ✅ | | | | ✅ |
| `orders:read` | ✅ | ✅ | | | ✅ |
| `orders:manage` | | ✅ | | | ✅ |
| `restaurants:read` | ✅ | ✅ | | | ✅ |
| `restaurants:update` | | ✅ | | | ✅ |
| `restaurants:create` | | | | | ✅ |
| `menu:read` | ✅ | ✅ | | | ✅ |
| `menu:manage` | | ✅ | | | ✅ |
| `users:provision` | | | | | ✅ |
| `drivers:read` / `drivers:update` | | | ✅ | | ✅ |
| `deliveries:read` | ✅ | | ✅ | | ✅ |
| `deliveries:manage` | | | ✅ | | ✅ |
| `deliveries:administer` | | | | | ✅ |
| `support:dashboard` | | | | ✅ | |
| `support-tickets:open` | ✅ | | | | ✅ |
| `support-tickets:read` | ✅ | | | ✅ | ✅ |
| `support-tickets:manage` / `:assign` | | | | ✅ | ✅ |
| `support-tickets:administer` | | | | | ✅ |
| `refunds:request` | | | | ✅ | ✅ |
| `refunds:approve` | | | | | ✅ |
| `support-analytics:read` | | | | ✅ | ✅ |
| `payment-methods:manage` / `payments:read` | ✅ | | | | ✅ |
| `payments:administer` | | | | | ✅ |

*An empty cell means the role does not hold that permission — and on this backend a missing
permission is a `403`, never a quietly narrower result. The two exceptions are deliberate: a
customer holding `orders:read` or `support-tickets:read` gets **their own** rows, because one
permission code cannot express "yours only" and the narrowing happens in the handler instead.*

Four consequences worth knowing before you design a screen:

1. **`carts:*` exists and no cart endpoint does.** The cart is genuinely browser-only state. That is
   not a gap to fill — it is the reason `CartService` in Milestone 1.3 is the state-management
   showcase.
2. **A SupportAgent cannot open a ticket.** `POST support/tickets` requires `support-tickets:open`,
   which is seeded to Customer and Administrator only. The endpoint's `onBehalfOfCustomerId` field
   — "an agent files a ticket from a phone call" — is therefore reachable by an administrator and
   not by an agent. Don't put that button on the agent portal.
3. **An Administrator does not join the `support` SignalR group.** `support:dashboard` is seeded to
   SupportAgent alone. An admin *does* hold `restaurants:update`, so the hub tries to put them in a
   restaurant group, finds no replica row, logs a warning and moves on. Both are correct; both will
   look like bugs if you don't expect them.
4. **A customer cannot read a driver record.** `GET delivery/drivers/{id}` needs `drivers:read`. The
   customer gets the driver's name from `DeliveryResponse.driverFirstName/LastName` or from the
   `DriverAssigned` hub frame's `driverName` — those are the only two sources.

---

## The two order-status vocabularies

The plan used to treat "order status" as one thing. It is two, and they are decoupled on purpose:
the REST read models expose the domain enum, and the socket has its own stable string contract
(`RealTime.Application/RealTime/OrderStatuses.cs`) whose comment says outright that it is
*"intentionally decoupled from any service's internal enum"*.

| REST — `OrderStatus` (a **number**) | Socket — `OrderStatuses` (a **string**) | Note |
|---|---|---|
| `Pending = 1` | `"Placed"` | **different name for the same state** |
| `Accepted = 2` | `"Accepted"` | |
| `Rejected = 3` | `"Rejected"` | |
| `Preparing = 4` | `"Preparing"` | |
| `ReadyForPickup = 5` | `"ReadyForPickup"` | |
| *(no equivalent)* | `"DriverAssigned"` | **socket-only** — see below |
| `OutForDelivery = 6` | `"OutForDelivery"` | published by Delivery, not Orders |
| `Delivered = 7` | `"Delivered"` | published by Delivery, not Orders |
| `Cancelled = 8` | `"Cancelled"` | |

Two traps live in that table:

- **`Pending` vs `Placed`.** A naïve `ORDER_STATUS_META` keyed by string will simply miss one of
  them, and the badge renders blank or falls through to a default. This is the classic "wrong badge
  in production" bug.
- **`DriverAssigned` has no REST equivalent.** When a driver accepts, the socket pushes
  `DriverAssigned` (with the driver's name and vehicle), but the Orders aggregate does not move —
  the order is still `ReadyForPickup = 5` and stays there until the driver marks pickup. So a
  customer who receives `DriverAssigned` and then refreshes the page will see the timeline *go
  backwards* unless you handle it. The delivery's own status (`DeliveryStatus.Assigned = 2`, via
  `GET delivery/orders/{orderId}/delivery`) is where that state actually lives.

**Design the shared map against the socket vocabulary and normalize REST into it**, because the
socket has strictly more states:

```ts
export type TimelineStatus =
  | 'Placed' | 'Accepted' | 'Preparing' | 'ReadyForPickup'
  | 'DriverAssigned' | 'OutForDelivery' | 'Delivered'
  | 'Rejected' | 'Cancelled';

export const ORDER_STATUS_META: Record<TimelineStatus,
  { label: string; tone: 'neutral' | 'progress' | 'success' | 'danger'; step: number | null }> = { /* … */ };

// The bridge. One function, one place to be wrong.
export function toTimelineStatus(rest: OrderStatus): TimelineStatus { /* Pending → 'Placed', … */ }
```

`step: null` for `Rejected` and `Cancelled` — they are not points on the line, they are terminal
banners.

A **separate** map handles `PaymentStatus`, because it is a second, orthogonal dimension:
`NotRequired = 1` (cash — for the whole life of the order), `Authorizing = 2`, `Authorized = 3`,
`Captured = 4`, `Released = 5`, `Failed = 6`. See Milestone 1.4 and Milestone 3.1.

---

## Backend prerequisites

Nine things block or degrade a real screen. Each is listed with the shape it needs, so the backend
work is a ticket rather than a discussion. #2 is done — it is kept below because the reasoning behind
the shape that was chosen is the useful part. Do #1 before frontend Phase 1; #3 before Milestone
1.1 step 5; #4 before Milestone 1.6 step 2. **#5–#9 were found while designing the screens**; none
blocks a milestone outright, because each screen degrades to showing an id instead of a name — but
each one is drawn on the canvas with a dashed amber **"needs backend · #n"** tag at the exact spot
it fills, so you can see what the screen loses without it.

### 1. CORS on Identity (the Gateway's half is **done**)

**Done:** `Cors:AllowedOrigins` in `Gateway/appsettings.Development.json` is already
`[ "http://localhost:4200", "https://localhost:4200" ]`, with `AllowCredentials: true` and
`ExposedHeaders: ["X-Correlation-Id", "Retry-After"]`. `AddEdgeCors`/`UseEdgeCors` sit in the
gateway pipeline *before* `UseAuthentication`, so preflights are answered without a token, and the
policy is applied to **every** proxied route — which includes `hubs/**`. The SignalR handshake is
covered, and so is the query-string token (`AddRealTimeHubAuthentication` in the RealTime host reads
`access_token` for `hubs/*` paths). **Nothing to do for the gateway or the realtime path.**

**Outstanding:** `Identity/Program.cs` registers no CORS at all — no `AddCors`, no `UseCors`, and no
Duende `AllowedCorsOrigins`. The SPA posts to `http://localhost:18080/connect/token` from
`http://localhost:4200`, which is cross-origin, so login fails at the preflight. Two acceptable
shapes:

- **(a)** Add `builder.Services.AddEdgeCors(builder.Configuration)` and `app.UseEdgeCors()` to the
  Identity host (before `app.UseIdentityServer()`) and give it the same `Cors:AllowedOrigins`
  section. Reuses the shared, already-tested component. *Recommended.*
- **(b)** Add a gateway route for `connect/{**catch-all}` (+ `.well-known/{**catch-all}`) with
  `AuthorizationPolicy: anonymous`, and drop the Identity base URL from the SPA entirely. Cleaner
  for the frontend — one base URL — but it contradicts `docs/security.md` §6.3, which states as a
  deliberate decision that token issuance does not pass through the gateway.

**How you'll recognise it:** a red request with no response and a CORS message in the console, with
a failed `OPTIONS` preflight just above it in the Network tab. That is this item, not your code.

### 2. The user's role must reach the client — **done, via `GET users/me`**

**The problem, verified:** the access token carries no role and no permission.
`CustomClaimsTransformation` adds `sub` and `permission` claims server-side per request; Identity
assigns no ASP.NET Identity roles (roles live only in the Users module's database); and
`ApiResource("fooddeliveryservice.api")` declares no `UserClaims`, so even name and email are absent
from the access token. Left alone, `roleGuard('Customer')` would have nothing to guard on and the app
could not decide which area to route a user to after login.

Two shapes were on the table. **(b) is what the Users module now exposes**; (a) was rejected, and it
is worth knowing why before you ever propose it in an interview.

- **(a) A `role` claim in the access token.** Add `UserClaims = { "role" }` to the `ApiResource` and
  an `IProfileService` that supplies it. Rejected for two reasons. Identity holds no role data at
  all, so it would have had to ask Users for it — and the platform forbids service-to-service HTTP
  except `api/users`. And a role baked into a token cannot be taken away before the token expires:
  demote an administrator and they keep administering for up to 15 minutes. That is precisely the
  property `CustomClaimsTransformation` exists to avoid, by resolving permissions per request.
- **(b) `GET users/me` on the Users module.** *Built.* Gated on `users:read`, which every role holds.
  Takes no parameters — the subject is the JWT, so there is nothing to pass and no way to ask about
  someone else. The response:

  ```jsonc
  // GET users/me  →  200
  {
    "userId":    "…",          // the MODULE-side id — the one on OrderResponse.customerId
                               // and the one the SignalR user:{id} group uses
    "email":     "…",
    "firstName": "…",
    "lastName":  "…",
    "roles":     ["Customer"]  // Role.Name values; empty array on a role-less account
  }
  ```

  **There is no `permissions` field** — an earlier draft of this plan hoped for one. The endpoint
  reads `users` + `user_roles` only, so anything permission-driven in the UI has to be derived from
  `roles` client-side (the table in [Roles,
  permissions…](#roles-permissions-and-which-portal-may-call-what) is the mapping, and it is seeded
  data you would be duplicating). A `404` is possible and means the account was deactivated after the
  token was minted — treat it as "log out", not as an error toast.

The SPA calls it once after login and once on session restore (Milestone 1.1 steps 1 and 6) and
caches the result in a signal. **It is UI and routing only.** No service trusts a role the client
read here; `PermissionAuthorizationHandler` still decides every request server-side, so the worst a
tampered `roles` signal buys you is a menu item that 403s when you click it.

### 3. The invitation email links to the API, not the SPA

`Notifications.Infrastructure/Email/EmailService.SendInvitationEmailAsync` builds:

```
{InvitationEmail:BaseUrl}/users/accept-invitation?email={email}&token={token}
```

with `InvitationEmailOptions.BaseUrl` defaulting to `http://localhost:3000` — the **gateway**. An
invitee clicking it gets a `405` from an endpoint that only accepts POST. The fix is two lines:
point `InvitationEmail:BaseUrl` at the SPA origin (`http://localhost:4200`) and change the hardcoded
path segment to the SPA route (`/auth/activate`). The query string is already exactly what the SPA
needs.

**Also:** there is no Mailpit or any other dev mail sink in `docker-compose.yml`. `EmailService`
*logs* the message — subject and body, activation link included. Read it from the Notifications
container's console output or from Seq at `http://localhost:8081`.

### 4. A provisioning endpoint for support agents and administrators

Today an administrator can provision exactly two kinds of account, and both are side effects of a
domain action: `POST restaurants` (which provisions the manager) and `POST delivery/drivers`. There
is **no HTTP route that provisions a SupportAgent**, so the support portal cannot be staffed from
the UI at all. The bus contract already exists and already takes the role —
`ProvisionUserRequest(email, firstName, lastName, role)`, consumed by
`Users.Presentation/Users/ProvisionUserRequestConsumer` — so the endpoint is a thin wrapper:

```
POST users/invitations          RequireAuthorization("users:provision")
{ "email": "…", "firstName": "…", "lastName": "…", "role": "SupportAgent" }
→ 200 { "userId": "…" }
```

`Role.Assignable` is `Customer | RestaurantManager | DeliveryDriver | SupportAgent`, and the
consumer already rejects `Administrator` and unknown names — so the endpoint inherits its own
validation.

### 5. Restaurant name and pickup address on the driver's offer and delivery

A driver holds neither `restaurants:read` nor `orders:read`, so `GET restaurants/{id}` returns
`403` and the offer card (sheet 37) and active delivery (sheet 38) can only say *"Restaurant
3f2a…"* and show a pin. Denormalise the pickup onto both responses, carried on whichever bus
contract creates the delivery (extend it if it does not already have the restaurant's name and
address):

```jsonc
// DeliveryOfferResponse and DeliveryResponse — add
"restaurantName":       "Pizzeria Bella",
"pickupStreet":         "Knez Mihailova 12",
"pickupCity":           "Beograd",
"pickupPostalCode":     "11000"
```

Without it: show "Pickup" plus the straight-line distance from `pickupLatitude/Longitude`, and a
"Open in Maps" link on the coordinates. Sheets 36–39 carry the `#5` tag.

### 6. Payment method and amount to collect on `DeliveryResponse`

For a `CashOnDelivery` order the driver must know how much to collect, and the driver cannot read
the order. Add to `DeliveryResponse` (not to the offer — a driver should not choose offers by
order value):

```jsonc
"paymentMethod":   "CashOnDelivery",   // or "Card"
"amountToCollect": 2167.00             // subtotal for cash, null for card
```

Without it the driver cannot tell a cash order from a card order — a real field-ops risk. The
"collect" strip on sheet 38 is drawn behind the `#6` tag (also on sheet 09) and is left out of the
build until this exists.

### 7. A customer display name on ticket responses

`TicketSummaryResponse` and `TicketResponse` carry `customerId` only, and an agent holds no
permission that resolves a user id to a name. The queue (sheet 40) and ticket header (sheet 42)
therefore show *"Customer 7c1e…"*. Add `customerDisplayName` (first name + last initial is enough)
to both, populated from the Users replica Support already keeps for authorship. Tagged `#7`.

### 8. `restaurantName` on the `SupportActivity` frame

The live activity feed (sheet 48) receives `{ orderId, restaurantId, status, occurredOnUtc }` and,
since agents hold no `restaurants:read`, cannot turn the id into a name. Add `restaurantName` to
the frame record. Tagged `#8`. Note the feed is **agents only** — administrators do not hold
`support:dashboard` and are not in the `support` group, so the admin shell does not show it.

### 9. An agent roster for "Assign to…"

`POST support/tickets/{id}/assign` takes an `agentId`, but nothing lists agents — there is no user
list at all. The design therefore offers **Claim** (self-assign) to agents, and "Assign to…" only to
administrators, and only as a disabled control until this exists:

```
GET support/agents        RequireAuthorization("support-tickets:administer")
→ 200 [{ "agentId": "…", "displayName": "Marko P.", "openTickets": 4 }]
```

Tagged `#9` on sheets 40–42.

### Nice-to-have (each unlocks a screen, none blocks a milestone)

- **Filtering on `GET restaurants`.** It takes `page` and `pageSize` and nothing else. A
  `?city=&cuisineType=&q=` trio would make a search screen worth building; see Milestone 1.2, which
  currently ships client-side filtering over the loaded page and says so out loud.
- **`DELETE` for menu categories and items.** Neither exists. A mistyped item is permanent (you can
  only mark it unavailable), which is a visible rough edge in a manager demo.
- **Opening hours on `RestaurantResponse`.** There is no such field and no endpoint that would set
  one. This is what killed the `FormArray` exercise; Milestone 1.5 re-homes it on something real.
- **Per-status timestamps on `OrderResponse`** (`acceptedOnUtc`, `readyOnUtc`, …). Only
  `placedOnUtc` exists, so the order timeline (sheet 22) prints a time only on the steps the
  backend keeps one for — Placed, plus the driver steps from `DeliveryResponse` — and none on
  Accepted/Preparing/Ready.
- **Totals on paged lists** (`X-Total-Count` or a `{ items, total }` envelope). Every list pages with
  Prev/Next only; "Page 2 of 7" is not drawn anywhere, by design.
- **Status and date filters on `GET orders`.** The manager's order history (sheet 35) has none.
- **Items on `OrderSummaryResponse`** — *verify first.* If the summary has no item lines, every
  incoming-order card on sheet 28 costs a `GET orders/{id}`.
- **Distinct error codes on `accept-invitation`** for expired vs. already-used vs. wrong-email
  tokens; today sheet 51 shows one generic "This link no longer works".
- **An invitation lookup** (`GET users/invitations/{token}` → the invitee's name and role), so the
  activation screen could greet the invitee. Sheet 51 deliberately shows only the email from the
  link.

---

## Coverage matrix — every backend feature and its real state

Backend feature numbering follows `FoodDelivery_ProjectPlan.md`. **State** is what is in the
repository on 2026-09-19, not what the plan intends.

| Backend feature | State | Frontend coverage |
|---|---|---|
| **1.1** Solution structure | ✅ shipped | `Frontend/` in the same monorepo; a `Frontend/**` job added to the existing `.github/workflows/ci.yml` — **0.A** |
| **1.2** Identity (registration, login, invitations, refresh, logout) | ✅ shipped | Login, customer registration, invitation activation, silent refresh, logout, session restore — **1.1**. Role routing reads `roles` from `GET users/me` ([prerequisite #2](#backend-prerequisites)), never from the token |
| **1.3** API Gateway | ✅ shipped (+ edge rate limiting, CORS, security headers from 3.7) | All REST **and** the WebSocket go through `:3000` — **all milestones**; 401/403/**429 with `Retry-After`** handled in **1.1** |
| **1.4** Restaurant Service | ✅ shipped — **no search, no filters, no opening hours, no ratings** | Paged browse + menu — **1.2**; manager menu/category CRUD and sold-out toggle — **1.5**; admin onboarding — **1.6** |
| **1.5** Order Service | ✅ shipped | Cart + checkout with idempotency key — **1.3**; order list/detail/timeline/cancel — **1.4**; manager accept → reject → preparing → ready — **1.5** |
| **1.6** Notification Service | ✅ shipped — **email only, zero HTTP endpoints** | The invitation-activation link target — **1.1**; a session-scoped in-app bell fed by SignalR, *not* an API — **2.1** |
| **1.7** CI/CD Phase 1 | ⚠️ never built as its own feature; CI first arrived with 2.5's Milestone A0 | Frontend job appended to the existing workflow — **0.A** |
| **2.1** Delivery Service & drivers | ✅ shipped | Full driver portal: availability, geolocation, the real offer contract, pickup/delivered, history — **2.2**; the customer's view of it — **2.3** |
| **2.2** SignalR real-time | ✅ shipped (5 hub methods; A–D done, optional Azure SignalR not built) | Realtime connection service + live status — **2.1**; driver offers — **2.2**; live map — **2.3** |
| **2.3** Redis caching | ✅ shipped | Backend-only. The frontend just gets faster menu reads. One README sentence, zero screens |
| **2.4** Telemetry & observability | ✅ shipped (A–E, G; optional Azure Monitor **not built**) | **No App Insights exists.** Browser-side contribution is correlation via the exposed `X-Correlation-Id` header — **3.4** |
| **2.5** Kubernetes / AKS | ⚠️ **scoped down** — plain `kubectl` manifests in `Backend/deploy/` only; Helm, HPA, Ingress, CI-deploy and AKS were cut | Backend-only. It does mean **there is no deployed backend to point a hosted SPA at** — see **3.4** step 6 |
| **2.6** Reviews & ratings | ❌ **not built** — no module, no endpoints, nothing | **Dropped.** See [what this plan deliberately does not build](#what-this-plan-deliberately-does-not-build) |
| **3.1** AI support chatbot (RAG) | ❌ **not built** | **Dropped.** The `TicketSource.Chatbot` enum member exists with nothing producing it |
| **3.2** Personalised recommendations | ❌ **not built** | **Dropped** |
| **3.3** AI-powered ETA | ❌ **not built** | **Dropped.** No ETA slot is designed into the tracking screen |
| **3.4** Fraud & anomaly detection | ⛔ **built, then reverted** (commit `6ae4879`, 2026-08-08) — only stale `bin/obj` artifacts remain, and the projects are out of the solution | **Dropped.** `TicketSource.FraudFlag` is the last trace of it |
| **3.5** Load testing | ✅ shipped | Backend-only (k6 targets the API). Frontend analogue: a Lighthouse performance budget — **3.4** |
| **3.6** Support Service & ticketing | ✅ shipped (A–C, E–G; H–I not built) | Customer-side tickets — **3.2**; agent/admin portal with queue, thread + internal notes, audit, refunds and analytics — **3.3** |
| **3.7** Production hardening | ✅ shipped — **except Milestone H (supply chain), which was cut**; the Azure cost model was cut with it | Frontend equivalent: a11y / perf / PWA / E2E / deploy / README pass — **3.4** |
| **3.8** Card payments (Stripe) | ✅ shipped (A–I, feature complete) | Saved cards via Stripe.js, card-vs-cash at checkout, the `PaymentStatus` badge on every order — **3.1**, with the badge itself from **1.4** |

---

## What this plan deliberately does not build

A deleted feature with a stated reason is an interview answer. A silent deletion is a gap. Each of
these was in the 2026-07-19 plan and is gone; the concept it was going to teach is either re-homed
or explicitly written off.

| Dropped | Why | Where the concept went |
|---|---|---|
| **Reviews & ratings** (old Milestone 2.4) and every rating reference in browse/detail | There is no review service — Feature 2.6 was never built. `RestaurantResponse` has no rating field and `GET restaurants` has nothing to sort or filter by | The accessible-radiogroup pattern the star component was for is re-homed on the **ticket category selector** (3.2) and the **refund decision** (3.3). Written off: partial-star clipping, cached-average reasoning |
| **Minimum-rating filter** | Same | — |
| **Restaurant search, cuisine filter chips, "near me" proximity** (old 1.2 steps 3–4) | `GET restaurants` accepts `page` and `pageSize`. There is no search parameter, no cuisine parameter and no coordinate parameter to send | `debounceTime` + `switchMap` + URL-as-state move to the **support ticket queue** (3.3), which really does filter server-side on `status`, `category`, `assignedAgentId`, `unassigned`, `from`, `to`. Browser geolocation survives, in the **driver portal** (2.2), where it was always the more honest use |
| **Opening hours editor** (old 1.5 step 3) | `RestaurantResponse` carries no hours and `PUT restaurants/{id}` accepts none. A `FormArray` of seven day-rows would post to nothing | The `FormArray` exercise moves to the **menu category reorder form** (1.5 step 2), which is a real `FormArray` of N rows over a real endpoint |
| **AI chatbot UI** (old 3.2) | Feature 3.1 was never built | Chat-bubble rendering and auto-scroll survive in the **ticket message thread** (3.2/3.3), which is a genuine two-sided conversation. Optimistic send does **not** — by design decision only the menu availability toggle is optimistic (sheet 60). Written off: consuming a streamed HTTP response — there is no streaming endpoint on this platform |
| **Recommendations carousel** (old 3.3 step 1) | Feature 3.2 was never built | Written off. `snap-x` carousels are a 20-minute skill; say so if asked |
| **Live ETA** (old 3.3 step 2, slot designed in 2.3) | Feature 3.3 was never built, and nothing on any DTO or hub frame carries an estimate | Written off. Do **not** design a placeholder slot for it — an empty "ETA: –" on a portfolio screenshot reads as an unfinished feature, not a planned one |
| **Fraud dashboard** (old 3.1 step 3) | Feature 3.4 was built and then reverted in `6ae4879`. The service is gone; only stale build artifacts remain | Written off. This one is worth mentioning in interviews as *"we built it and took it back out"* — knowing why a thing was removed is the more interesting half |
| **Review moderation** (old 3.1 step 4) | Depends on reviews, which do not exist | Written off |
| **A notifications API / persisted notification list** | The Notifications service has never exposed an HTTP endpoint | The bell becomes a **session-scoped feed of socket events** (2.1 step 5) — honest, and the honesty is the point |
| **`users/profile`** (old 1.1 steps 1 & 6) | No such endpoint, and no way to *edit* a profile over HTTP — `users:update` exists as a permission but nothing maps it to a route | Read-side only, via `GET users/me` ([prerequisite #2](#backend-prerequisites)). The profile screen renders name, email and roles and offers no save button |
| **Application Insights browser SDK** (old 3.4 step 7) | There is no Application Insights anywhere in the backend | Replaced by correlation on the `X-Correlation-Id` response header, which the gateway's CORS policy already exposes by name — **3.4** |
| **Web push notifications** (old 3.4 step 8) | Would need a subscription endpoint and a push sender in Notifications; neither exists, and neither is a small change | Written off. The PWA install (3.4 step 1) stays; push does not |
| **Client-side restaurant filter box** (old 1.2) | Filtering only the loaded page silently hides restaurants on later pages, which reads as "not on the platform". The design brief rules it out | Written off. The restaurant list is a plain paged list (sheet 14) |
| **Delete for menu items/categories, "Assign to…" for agents, a call-the-driver button, per-step order times** | No DELETE endpoint, no agent roster (prerequisite #9), no phone number on any DTO, no per-status timestamps (nice-to-have) | Not drawn. Availability toggle instead of delete, Claim instead of assign, times only where the backend keeps them |

---

## UI design reference

The screens are designed on the canvas **Food Delivery Platform UI**
(<https://claude.ai/artifact/LBMxyGjFyx1hv9iCnJpZDS>, 61 numbered sheets; the standalone export
`food-delivery-ui-sheets.html` has the same sheets offline). Build from the sheet, check against
this plan.

**Who wins when they disagree:**

- **This plan wins on the contract** — endpoints, fields, enums, permissions, frames, error shapes.
  If a sheet shows data the API does not return, the sheet is wrong; fix the sheet.
- **The design wins on UX decisions** taken during design. These were deliberate and override
  older wording in this plan:
  1. **Only the menu availability toggle is optimistic** (sheet 60). Everything else — sending a
     ticket message included — waits for the `2xx` and shows a pending state.
  2. **The tax id is read-only for the manager** (sheet 34), even though `PUT restaurants/{id}`
     accepts it — it is administrator-owned data, set at onboarding.
  3. **No client-side search or filter box** over paged lists. Server-side filters (the ticket
     queue's) stay.
  4. **The refund reject note is required on the client** (sheet 55), even though the API body is
     `{ note? }` — a rejection without a reason is unauditable.
  5. **No ETA anywhere, no call button anywhere** (sheets 22–24, 38).
- **A dashed amber "needs backend · #n" tag** marks a spot that depends on prerequisite #n. Build
  the fallback described under that prerequisite, not the tagged content.

| Sheets | What | Milestone |
|---|---|---|
| 01–08 | Tokens, type & density, buttons, inputs, surfaces, feedback, motion & a11y, status legend | 0.B |
| 09–11 | Mobile shell, desktop (admin) shell, role-driven navigation | 0.C, 2.1 |
| 12–13 | Connection state, notification bell | 2.1 |
| 14–15 | Restaurant list, menu | 1.2 |
| 16–20 | Cart, cart conflict, checkout, card declined, item changed | 1.3 (19 with 3.1) |
| 21–23 | Order list, order detail timeline, terminal branches | 1.4 |
| 24 | Live tracking | 2.3 |
| 25 | Payment methods | 3.1 |
| 26, 61 | My tickets, open a ticket | 3.2 |
| 27 | Profile | 1.1 |
| 28–35 | Incoming orders, arrival & authorising, states, reject modal, menu admin, item form, restaurant profile, order history | 1.5 |
| 36–39 | Driver home, offer, active delivery, history & profile | 2.2 |
| 40–48 | Ticket queue, queue states, ticket detail, composer, audit trail, request refund, analytics, young dataset, live activity | 3.3 |
| 49–51 | Login, register, accept invitation | 1.1 |
| 52–54 | Onboard restaurant, manager & confirmation, onboard driver (the support-agent form reuses 54) | 1.6 |
| 55–57 | Refund decisions, approve confirmation, refund outcomes | 3.3 |
| 58 | Platform overview | 1.6 |
| 59–60 | Error taxonomy, loading & optimistic patterns | 0.B, 3.4 |

---

# Part 2 — Detailed Implementation Plan

Each milestone lists: **Goal → Steps → New concepts you learn → 💡 Hints → Done when.**
Milestones are ordered; each builds on the previous. Estimated effort assumes evenings/weekends
alongside backend work — treat estimates as loose.

---

## Phase 0 — Foundations

> **Goal:** a running, styled, linted, tested-once, deployed-nowhere app skeleton with routing and
> a shared UI kit. No backend calls yet. This phase front-loads all tooling pain so every later
> milestone is pure feature work.

### Milestone 0.A — Workspace & tooling (½–1 day)

**Steps**
1. Install the current LTS Node.js and the Angular CLI. Run `ng new food-delivery-web` inside
   `Frontend/` — choose **CSS** (Tailwind replaces SCSS), routing **yes**, SSR **no**.
2. Add Tailwind CSS v4 (per the official Angular guide: install, add `@import "tailwindcss";` to
   `styles.css`).
3. Add angular-eslint (`ng add angular-eslint`) and Prettier with the Tailwind class-sorting
   plugin (`prettier-plugin-tailwindcss`). Add npm scripts: `lint`, `format`.
4. Enable TypeScript strict options (the CLI default already is strict — verify, don't weaken).
5. Create `environments/` with the **two** backend base URLs: `gatewayUrl`
   (`http://localhost:3000`) and `identityUrl` (`http://localhost:18080`).
6. Verify `ng test` runs the default Vitest suite and `ng build` produces a production build.
7. Commit. Add a **frontend job to the repo's existing `.github/workflows/ci.yml`** — install →
   lint → test → build, `paths`-filtered to `Frontend/**`. Do not start a second workflow file.

**New concepts:** Angular CLI, the dev server, project configuration, how a frontend "build" works
(TypeScript → bundled/minified JS), CI for frontend.

**💡 Hints**
- *Step 1:* run `ng new food-delivery-web --style=css --ssr=false` from inside `Frontend/`. Check
  `node -v` first — the CLI tells you which versions it supports; on Windows use `nvm-windows` if
  you need to switch Node versions.
- *Step 2:* Tailwind v4 has **no `tailwind.config.js`** by default — configuration lives in CSS
  (`@theme inline { … }` in `styles.css`, mapping the variables in `styles/tokens.css` — see 0.B).
  If utility classes have no effect: check the `@import
  "tailwindcss";` line is first in `styles.css`, then restart `ng serve` (config changes aren't
  always hot-reloaded).
- *Step 3:* set VS Code `"editor.formatOnSave": true` + Prettier as default formatter now; run
  `npx prettier --write .` once so the first real commit isn't polluted by formatting noise.
- *Step 5:* if the CLI didn't scaffold environments, `ng generate environments` creates them plus
  the `fileReplacements` build config. Type the environment object (`interface Env`) so a typo in
  a URL key is a compile error. **Two keys, not three** — if you find yourself adding a
  `realtimeUrl`, re-read [How the frontend talks to the backend](#how-the-frontend-talks-to-the-backend).
- *Step 7:* the existing workflow's jobs run from the repo root with `SOLUTION: Backend/…`; yours
  needs `defaults.run.working-directory: Frontend/food-delivery-web`. Use `actions/setup-node` with
  `cache: 'npm'` and run `npm ci` (not `npm install` — `ci` respects the lockfile exactly, like
  `dotnet restore --locked-mode`). Add `paths: ['Frontend/**']` so a backend-only PR doesn't run it.
- Install the **Angular DevTools** browser extension today — you'll use its component tree and
  signal inspection constantly.

**Done when:** `ng serve` shows a page styled by a Tailwind class; CI is green on GitHub and the
frontend job is visibly skipped on a backend-only commit.

### Milestone 0.B — Design tokens & shared UI kit (2–3 days)

**What & why:** Before building screens, build the LEGO bricks. A small set of reusable components
gives every later screen a consistent look and teaches component API design — the single most
transferable Angular skill.

> 🎨 **Design:** sheets 01–08 are this milestone — tokens (01), type & density (02), buttons (03),
> inputs (04), surfaces (05), feedback (06), motion & a11y (07), the status legend (08). Sheets 59
> (error taxonomy) and 60 (loading & optimistic) define how `app-toast`, the inline error and the
> skeletons behave; build the components to those rules now rather than retrofitting in 3.4.

**Steps**
1. **Tokens (done — sheets 01, 02, 07).** `src/styles/tokens.css` holds the design tokens as plain
   `:root` CSS variables, imported once *before* Tailwind in `styles.css`:
   - brand scale `--brand-50…900` (brand-600 `#b8431f` is the action color) and a warm neutral
     ramp `--n-0…950`;
   - role tokens (`--bg-base`, `--surface*`, `--border*`, `--text*`, `--accent*`, `--focus-ring`) —
     components use roles, never raw ramp steps;
   - four semantic colors, `success / warning / danger / info`, each with `-subtle`, `-border`
     and `-text` variants;
   - radii `--r-sm…xl` (4/8/12/16) and `--r-full`, shadows `--sh-*`, 4px spacing scale `--sp-*`,
     z-layers `--z-*`, motion `--dur-*` / `--ease-*`;
   - type & density: Plus Jakarta Sans (`--font-sans`) plus JetBrains Mono (`--font-mono`, ids and
     prices), loaded from Google Fonts in `index.html`; `[data-density='compact']` swaps body
     size, row and control heights;
   - `[data-theme='dark']` overrides the *roles* and semantics (the ramps don't change);
   - one global `prefers-reduced-motion` opt-out.

   They are deliberately **not** defined under `@theme`: `@theme` values are frozen at build time,
   so dark theme and density couldn't swap them at runtime. Instead `styles.css` has a
   `@theme inline` block mapping them to utilities (`bg-surface`, `text-accent`, `border-border`,
   `rounded-lg`, `shadow-md`, `bg-success-subtle`…) that emit `var(--x)`. Also in `styles.css`: body
   defaults, a `:focus-visible` ring, and a `num` utility (`tabular-nums`) for prices, quantities,
   ids and countdowns.
   **Still to build:** a `ThemeService` that sets `data-theme` / `data-density` on `<html>` before
   first paint (persisted choice, falling back to `prefers-color-scheme`).
2. Build in `shared/ui/`, one at a time, each a standalone component using `input()` / `output()`
   signal functions:
   - `app-button` (variants: primary/secondary/danger; sizes; `loading` state that disables + spins)
   - `app-input` (label, error message slot — designed to work with Reactive Forms)
   - `app-card`, `app-badge` (used for order **and payment** statuses everywhere), `app-spinner`,
     `app-empty-state` (icon + message + optional action)
   - `app-modal` (confirm dialogs) and a `ToastService` + toast container (global notifications)
3. Create a throwaway `/styleguide` route that renders every component in every variant — your
   manual test page (keep it; it's impressive in a portfolio walkthrough).
4. Mobile check: open devtools device emulation (iPhone SE, 375 px) — everything must look right
   at that width *first*.

**New concepts:** component inputs/outputs with signals, content projection (`ng-content`),
`@if`/`@for` control flow, host bindings, Tailwind utility composition, mobile-first workflow.

**💡 Hints**
- *Step 2 (component APIs):* declare inputs like `variant = input<'primary' | 'secondary' |
  'danger'>('primary')` and build the class string in a `computed()`. Union types instead of
  strings = autocomplete + compile errors for callers.
- *Step 2 (badge):* give it a `tone` input rather than a `status` input. It will render order
  statuses, payment statuses, ticket statuses, refund statuses and delivery statuses — five
  unrelated enums — and a badge that knows about any of them is a badge you'll rewrite. Map
  enum → tone at the call site, via the `*_STATUS_META` constants.
- *Step 2 (button):* one `app-button` gotcha — a `loading` button must also set `disabled` and
  keep its width (reserve space for the spinner) so the layout doesn't jump.
- *Step 2 (modal):* build it on the native `<dialog>` element — you get focus trapping, ESC to
  close, and a backdrop for free (`dialog.showModal()`); styling via `::backdrop`.
- *Step 2 (toast):* `ToastService` = a `signal<Toast[]>` plus `setTimeout` to auto-remove; render
  one `<app-toast-container>` in `app.component.html`. Position `fixed bottom-20` on mobile so
  toasts don't collide with the bottom nav.
- *Step 2 (icons):* don't add an icon library/font — copy the handful of SVGs you need from
  [heroicons.com](https://heroicons.com) or Lucide straight into small components. Set
  `class="size-5"` and `stroke="currentColor"` so they scale and inherit color.
- *Step 4:* prefer `min-h-dvh` over `h-screen` for full-height mobile layouts — `100vh` is buggy
  under mobile browser URL bars; `dvh` (dynamic viewport height) is the fix.
- If a component needs more than ~5 inputs, stop — split it or pass an object. Fat component APIs
  are the frontend version of a fat constructor.

**Done when:** the styleguide page looks clean at 375 px and 1440 px; components are used (not
copies of markup) by everything that follows.

### Milestone 0.C — App shell, routing skeleton & fake auth (2–3 days)

> 🎨 **Design:** sheet 09 (mobile shell — customer and driver), sheet 10 (desktop shell, drawn as
> the Administrator's: Platform / Provisioning / Support sections) and sheet 11 (navigation built
> from `roles` — one role gets one area with no switcher; two roles get an area switcher in the
> profile menu).

**Steps**
1. Define top-level routes with lazy loading: `/auth/**`, `/customer/**` (also the default),
   `/restaurant/**`, `/driver/**`, `/admin/**`, `/support/**`, plus a `**` not-found page. Each
   feature gets a `<feature>.routes.ts` file loaded via `loadChildren`.
2. Build two layout components in `core/layout/`:
   - **Mobile-first shell** (customer & driver): sticky top bar + **bottom tab navigation** —
     the standard mobile app pattern (Home, Orders, Cart, Profile for customers).
   - **Desktop shell** (restaurant/admin/support): collapsible left sidebar + top bar.
3. Create `AuthService` with a **hard-coded fake user** switchable from a dev-only dropdown in the
   header (Customer / Manager / Driver / Support / Admin). Implement `authGuard` and `roleGuard`
   against the fake user. Real backend auth replaces the internals in Milestone 1.1 — the guards,
   layouts, and role routing won't change.
4. Placeholder pages ("Restaurants coming soon") for each area's landing route to prove guards and
   lazy loading work.

**New concepts:** lazy loading, route guards (`CanActivate` functions), router layouts with
`router-outlet`, active-link styling, redirect logic by role.

**💡 Hints**
- *Step 1:* the lazy-load pattern is
  `{ path: 'customer', loadChildren: () => import('./features/customer/customer.routes').then(m => m.CUSTOMER_ROUTES) }`.
  Each area's routes file exports a `Routes` array whose root route holds the layout component
  with child routes inside it.
- *Step 2 (layouts):* the layout component is just a shell with `<router-outlet />` in the middle;
  the bottom nav uses `routerLink` + `routerLinkActive` for the active tab. Give the fixed bottom
  nav `pb-[env(safe-area-inset-bottom)]` so it clears the iPhone home indicator.
- *Step 3 (guards):* write functional guards, and **return a `UrlTree` instead of calling
  `router.navigate`** — `export const authGuard: CanActivateFn = () =>
  inject(AuthService).isLoggedIn() ? true : inject(Router).createUrlTree(['/auth/login']);`
  Returning the UrlTree lets the router handle redirect + history correctly. Make `roleGuard` a
  factory: `roleGuard('Customer')` returns a `CanActivateFn`.
- *Step 3 (fake auth):* keep the fake user in `signal<User | null>` with the **exact shape
  `GET users/me` returns** — `{ userId, email, firstName, lastName, roles: string[] }`, and nothing
  else (see [prerequisite #2](#backend-prerequisites); there is no `permissions` field, so don't
  invent one here and discover it in 1.1). Copying the real shape is what makes the 1.1 swap a
  one-line change to where the signal is filled from. Show the role-switcher only when `isDevMode()`
  is true.
- *Step 3 (role names):* they are exactly `Administrator`, `Customer`, `RestaurantManager`,
  `DeliveryDriver`, `SupportAgent` (`Users.Domain/Users/Role.cs`). Type them as a union, not
  `string`.
- *Step 4:* verify lazy loading in devtools Network tab (filter JS): navigating to `/restaurant`
  the first time should fetch a new chunk file. If everything loads upfront, you used a static
  `import` somewhere in a routes file.

**Done when:** switching the fake role and navigating shows the correct shell per role, blocks the
wrong areas, and the network tab shows each area's JS chunk loading only on first visit.

---

## Phase 1 — Core Features

> **Goal:** the app works end-to-end against the real backend: register → log in → browse
> restaurants → order (cash) → restaurant accepts → status visible. After this phase the project is
> already demo-able. Cards, live sockets and support tooling come later; none of them is needed for
> a complete order.

### Milestone 1.1 — Real authentication (3–5 days)

**What & why:** The frontend's front door, wired to Duende exactly as described in the
[architecture section](#authentication-design-matches-the-existing-backend-exactly).

> 🎨 **Design:** sheet 49 (login, including the lockout line), 50 (register, with the four
> character-class chips under the password), 51 (accept invitation), 27 (profile, read-only from
> `users/me`).

> 📌 **Read [backend prerequisite #2](#backend-prerequisites) first.** Steps 1 and 6 both call
> `GET users/me`, and the two-ids trap it describes is the single easiest way to lose a day in this
> milestone. `roles` comes from that response and from nowhere else — never infer one from the email
> address or from the route you happened to land on. That kind of guess is exactly how a driver ends
> up looking at the admin portal.

**Steps**
1. **DTOs & `AuthService`:** implement `login(email, password)` posting the password-grant form to
   `{identityUrl}/connect/token`. Decode the JWT payload (base64url — no library needed) and look at
   what is actually in it: `sub` (the **Duende identity id**, not the module user id), `client_id`,
   `scope`, `aud`, `exp`. **No role, no permission, no email.** Then call `GET users/me` through the
   gateway for the real user — `{ userId, email, firstName, lastName, roles }` — and hold it in a
   signal. Expose `currentUser`, `isLoggedIn` and `hasRole()` as signals/computed. No
   `hasPermission()`: the response carries no permission list, so gate UI on roles and let the 403
   handling in step 2 be the honest fallback for anything finer.
2. **Interceptors:** `authInterceptor` adds the bearer token to gateway requests only (never to the
   identity host); `errorInterceptor` maps ProblemDetails → `ApiError` (`{ code, detail, status,
   errors? }` where `code` is the response's `title`), toasts unexpected errors. On 401: refresh
   once, replay, else logout. On **429**: read the `Retry-After` header and show an **inline countdown
   that retries automatically** when it reaches zero (sheet 59) rather than a generic failure — and
   **never auto-retry a checkout `POST orders`**; there the button just re-enables after the
   countdown. This is the hardest code in the whole app — take it slow.
3. **Login page:** typed reactive form, validation messages via `app-input`, loading button,
   "invalid credentials" handling, redirect to role home on success. Mention lockout in the error
   copy — five failures really does lock the account for 15 minutes. Sheet 49 shows a permanent
   one-line hint under the form, not a counter: the API does not tell you how many attempts are
   left.
4. **Customer registration page:** posts to gateway `POST users/register` (anonymous) with
   `{ email, password, firstName, lastName }`, then auto-login. Mirror the backend's password rules
   client-side for fast feedback, but remember the backend is the source of truth — and note that
   **Development relaxes them to a 1-character minimum** while production requires 12 plus the
   default character classes (`Identity/Program.cs`). Validate against the production rules; a form
   that accepts `a` locally and fails in a demo is worse than one that's strict everywhere. Sheet 50
   renders the rule as a length hint plus four class chips (upper, lower, digit, symbol) that tick
   as the user types.
5. **Invitation activation page** (`/auth/activate`): reads `email` and `token` from the query
   string, shows the email read-only (there is no invitation lookup, so no name or role to greet
   with), lets the invitee set a password, posts to
   `POST users/accept-invitation { email, token, newPassword }` (anonymous, returns `204`), then
   **signs in with the same email and password** and routes to the role home — two calls, one
   screen (sheet 51). Any `4xx` from the first call shows one generic "This link no longer works"
   state; the API does not distinguish expired from used. Depends on [prerequisite #3](#backend-prerequisites) for the email to link here
   at all — until then, paste the link's query string by hand from the Notifications logs.
6. **Logout** + session restore on app start: validate the stored token, re-fetch `users/me`,
   *then* let the router start.
7. Replace the fake auth internals from 0.C; keep the dev role-switcher working via seeded test
   accounts (the dev admin from backend configuration, plus accounts you create).

**New concepts:** typed reactive forms + validators, HTTP interceptors, JWT anatomy from the
client side, RxJS `switchMap`/`catchError` in the refresh flow, query params, app initialization.

**💡 Hints**
- *Step 1 (token request):* the token endpoint wants `application/x-www-form-urlencoded`, **not
  JSON** — pass an `HttpParams` object as the POST *body* and HttpClient sets the content type for
  you. Sending JSON produces `unsupported_grant_type`/`invalid_request` errors that look like
  backend bugs but aren't.
- *Step 1 (JWT decode):* the payload is **base64url**, not plain base64 — `atob` alone breaks on
  `-`/`_` characters. Write a 5-line helper that replaces them (`-`→`+`, `_`→`/`) before `atob` +
  `JSON.parse`. No JWT library needed.
- *Step 1 (the two ids):* the JWT's `sub` and `users/me`'s `userId` are **different values**. The
  module-side `userId` is the one that matches `OrderResponse.customerId`, `TicketResponse
  .customerId` and the SignalR `user:{id}` group. Name them `identityId` and `userId` in your model
  and never let them touch.
- *Step 2 (interceptors):* use functional interceptors (`HttpInterceptorFn`) registered via
  `provideHttpClient(withInterceptors([...]))`. Skip attaching the bearer token when the URL is
  the identity host (sending a stale token with a refresh request is a classic loop-starter).
- *Step 2 (refresh loop protection):* mark replayed requests with an `HttpContextToken` so a 401
  on the *retried* request logs out instead of refreshing forever.
- *Step 2 (single-flight refresh is not optional here):* the Duende client sets
  `RefreshTokenUsage = OneTimeOnly`, so two simultaneous refreshes invalidate the whole chain and
  log the user out. Share one in-flight refresh with `shareReplay(1)`. On most backends the naive
  version merely wastes a request; on this one it is a bug you *will* hit when a page fires three
  parallel GETs after a 15-minute idle.
- *Step 2 (`X-Correlation-Id`):* the gateway's CORS policy exposes it by name, so the browser can
  read it. Stash the last one in your `ApiError` and render it in the toast's detail — "reference
  `a1b2c3`". You can then grep Seq for exactly that request. This is the cheapest full-stack debug
  tool you will ever build, and it takes four lines.
- *Step 2 (debugging CORS):* a red request with no response and a console message about CORS is
  [prerequisite #1](#backend-prerequisites), not your code — look for the failed `OPTIONS`
  preflight in the Network tab.
- *Step 3/4 (forms):* build with `NonNullableFormBuilder` (`inject(NonNullableFormBuilder)`) so
  values are typed without `| null` everywhere. Show a field's error only after `touched` — wire
  that logic once into `app-input`, not per page.
- *Step 5:* read the query string with router input binding (`withComponentInputBinding()` in
  `provideRouter`, then `token = input<string>()` in the page).
- *Step 6:* register session restore with `provideAppInitializer(...)` in `app.config.ts` so the
  router doesn't start before you know whether the user is logged in (otherwise guards redirect
  to login on every F5).
- *Testing refresh:* the access token already lives for only 15 minutes — leave a tab open over
  lunch and watch the Network tab do a token call mid-session, invisible to the UI. Lower it in the
  Duende client config if you want it in 60 seconds.

**Done when:** all auth flows work against the running backend; refresh is observable in the network
tab; a full page reload keeps you logged in; and logging in as a driver lands you on `/driver`, not
on the customer home.

### Milestone 1.2 — Customer: browse restaurants & menus (2–3 days)

> **Scope note, read it before you start.** `GET restaurants` takes `page` and `pageSize` and
> nothing else, and `RestaurantResponse` carries no rating, no opening hours and no distance. This
> milestone is therefore smaller than it was in the previous plan, and it is honest about it. The
> debounced-server-search exercise moves to Milestone 3.3, which has an endpoint that can actually
> take filters; browser geolocation moves to Milestone 2.2, where a driver's position genuinely
> matters.

**Steps**
1. `RestaurantsApi` client + DTOs for `GET restaurants`, `GET restaurants/{id}` and
   `GET restaurants/{restaurantId}/menu`, hand-written from
   `http://localhost:3000/docs/restaurants/scalar`.
2. **Restaurant list page** (customer home, sheet 14): a list of text rows — name, cuisine type,
   city — one column on phone, two on desktop. Server-side pagination via a "Load more" button —
   simpler than infinite scroll, fine for a portfolio, and the only thing the endpoint supports. The
   top bar says the product name, not "Deliver to…": there is no saved address to show.
3. **No filter box — by design decision.** An earlier version of this plan had a client-side
   filter over the loaded page. The design dropped it: filtering one page silently hides
   restaurants on page 2, which a customer reads as "not on the platform". Write one sentence in the
   README saying the backend has no search parameter yet — see the nice-to-have in [backend
   prerequisites](#backend-prerequisites) — and that search arrives with it, server-side.
4. **Restaurant detail / menu page (sheet 15):** header with name, cuisine, address and phone; menu grouped by
   category (ordered by `displayOrder`) with a sticky category tab bar; items showing name,
   description, price and photo; `isAvailable: false` items visibly disabled and unclickable.
5. Loading skeletons and `app-empty-state` for no-results; error state with retry.

**New concepts:** container/presentational split in practice, `computed()` as a derived view of
server state, skeleton loading UX, rendering nested backend data, sticky positioning.

**💡 Hints**
- *Step 2 (grid):* `grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4` is the whole layout.
  For "Load more", append to a `signal<Restaurant[]>` and track the next page number — resist
  infinite scroll (IntersectionObserver) until everything else works.
- *Step 2 (page size):* the validator caps `pageSize` at 100 and requires `page >= 1`; send 0 or
  101 and you get a 400 with a `ValidationError` body. Worth triggering once on purpose so you see
  the `errors` extension member your interceptor parses.
- *Step 2 (no logo field):* `RestaurantResponse` has no image. The design does not fake one —
  rows are typographic (sheet 14). Don't add initial-letter tiles; they read as missing photos.
- *Step 3 (where the computed-filter exercise went):* URL-as-state is the right pattern for a
  *server* filter, and you'll do it properly on the ticket queue in 3.3.
- *Step 4 (sticky tabs):* `sticky top-0 z-10` on the tab bar + `scrollIntoView({ behavior:
  'smooth' })` on tab click gets you 90% of the effect; highlighting the active section while
  scrolling needs `IntersectionObserver` — skip it if it fights back.
- *Step 4 (menu shape):* the whole menu — every category and every item — arrives in **one**
  `GET .../menu` call. There is no per-category endpoint and no paging. It is also Redis-cached
  server-side and evicted inline on every menu write, so a manager's change shows up on the very
  next read, not an outbox hop later.
- *Step 5 (skeletons):* gray boxes matching the card's real dimensions + Tailwind's
  `animate-pulse`. Show them on *initial* load only; "Load more" gets a spinner on the button
  instead.

**Done when:** you can find a seeded restaurant on a phone-sized screen in under three taps, the
menu renders grouped and ordered with sold-out items visibly dead, and the README says in one
sentence why there is no search box.

### Milestone 1.3 — Customer: cart & checkout (3–4 days)

**Steps**
1. **`CartService`** — the state-management showcase, and genuinely the only place this app owns
   state the server does not: `signal<CartItem[]>`, `computed` subtotal / count, add/remove/
   change-quantity methods, persisted to `localStorage`, one-restaurant-per-cart rule (adding from
   another restaurant prompts to clear — standard UX). Sheets 16 (cart) and 17 (cart conflict).
2. **Menu integration:** add-to-cart buttons with quantity steppers; cart icon with a `computed`
   item-count badge in the shell.
3. **Cart page/drawer:** line items, edit quantities, and a **subtotal**. Not a "total" — see the
   box below.
4. **Checkout page (sheet 18):** delivery address form (typed reactive form for `street`, `city`,
   `postalCode`, `country`, optional `notes`), payment method **fixed to "Cash on delivery"** in
   this milestone, order summary. On submit: generate an **idempotency key**
   (`crypto.randomUUID()`, created *when checkout opens*) and send it as the `Idempotency-Key`
   header. Add a **"Pin my location"** button that fills only `latitude`/`longitude` from the
   browser's geolocation (no reverse geocoding — the street fields stay the customer's own words);
   a small "Location pinned" chip confirms it.
5. **Menu pre-flight (sheet 20):** when checkout opens, re-fetch `GET restaurants/{id}/menu` and diff
   it against the cart — price changed, item now unavailable, item gone. Show the changed lines
   before the customer presses "Place order", not after. The server refuses only *unavailable*
   items; a changed price is silently re-priced, so the pre-flight is the only way the customer
   learns of it.
6. **Order placement** via `POST orders` → the response is a bare `Guid` → success screen → clear
   cart → link to order detail. Handle the failure cases: an item that went unavailable between
   pre-flight and submit (highlight the line, sheet 20), or a menu item id the server no longer
   prices. *(From 3.1 on, a card order clears the cart only once `paymentStatus` reaches
   `Authorized` — sheet 19.)*

> 💰 **There is no total, and inventing one is the trap.** `OrderResponse` has `subtotal` and
> `commissionRate` and nothing else — no delivery fee, no tax, no tip, no grand total.
> `commissionRate` (a fraction, e.g. `0.20`) is the **platform's** cut of the subtotal; it is
> business-side, and adding it to what the customer sees would overstate their bill by 20%. Render
> "Subtotal" and stop. If a reviewer asks where the delivery fee is, the answer is that the backend
> does not model one — which is a better answer than a number you made up.

**New concepts:** shared client state with signals (the heart of frontend thinking — state that
exists only in the browser), `effect()` for localStorage persistence, optimistic vs. confirmed UI,
idempotency from the client side.

**💡 Hints**
- *Step 1 (persistence):* one `effect(() => localStorage.setItem('cart',
  JSON.stringify(this.items())))` in the service constructor persists every change automatically.
  Hydrate in the constructor with a `try/catch` around `JSON.parse` — a corrupt value must clear
  the cart, not crash the app.
- *Step 1 (money):* JavaScript floats will happily tell you `0.1 + 0.2 = 0.30000000000000004`.
  Keep prices as the numbers the backend sends, do arithmetic in a `computed`, and round **only**
  at display time in your money pipe (`Intl.NumberFormat`). Never accumulate rounded values.
- *Step 1 (one-restaurant rule):* store `restaurantId` on the cart itself; on add-from-elsewhere,
  open the confirm modal and clear on confirm — copy the UX of any big delivery app.
- *Step 1 (prices are advisory):* your cart holds prices only so the customer can see a subtotal.
  The `POST orders` body carries **`menuItemId` and `quantity` only** — the server prices every
  line from its own replica. Sending a price is impossible, which is a nice thing to be able to say
  in an interview about client-supplied data.
- *Step 4 (idempotency key):* create it with `crypto.randomUUID()` **when the checkout page opens**
  (a component field), not inside the submit handler — a double-click must reuse the same key,
  that's the entire mechanism. The header name is exactly `Idempotency-Key`.
- *Step 4 (double-submit):* also set a `submitting` signal that disables the button; the
  idempotency key is the backend guarantee, the disabled button is the UX guarantee. You want
  both, and you can name that distinction in interviews.
- *Step 4 (paymentMethod is a string):* the field is declared `string` and defaults to
  `"CashOnDelivery"`. Send the enum **member name**, not `1`. Milestone 3.1 makes `"Card"` real.
- *Step 4 (lat/lng are optional):* `deliveryAddress.latitude`/`longitude` are `double?`. They are
  what the Delivery service uses to find a nearby driver and to draw the drop-off pin, so an order
  placed without them still works but tracks poorly — which is why the design gives them a button
  rather than leaving them null. `navigator.geolocation.getCurrentPosition` with a denied-permission
  fallback that simply leaves them null.
- *Step 6 (failures):* match the ProblemDetails `title` (the code, e.g. `Orders.MenuItemNot
  Available`) against cart lines and highlight them, instead of a generic toast. This is the first
  place your error-handling design pays off — and the first place you'll be glad `title` is a code
  and not a sentence.

**Done when:** the classic demo works end-to-end: browse → add items → checkout → order row exists
in the backend DB; double-clicking "Place order" creates exactly one order; and the cart survives a
refresh.

### Milestone 1.4 — Customer: my orders & order detail (3–4 days)

> 🎨 **Design:** sheet 21 (order list), 22 (order detail — a seven-step timeline: Placed, Accepted,
> Preparing, Ready, Driver assigned, Out for delivery, Delivered), 23 (terminal branches: rejected,
> cancelled, payment failed).

**Steps**
1. **Orders list page:** `GET orders` (self-scoped, `page`/`pageSize` only — there is no status
   filter to offer, so sort client-side into "active" and "past" from the status you already have).
   Each row shows **two badges**: the lifecycle status and the payment status.
2. **The two-badge rule.** `OrderStatus` and `PaymentStatus` are orthogonal dimensions on every
   order, by explicit backend design. A cash order is `NotRequired` for its entire life, so its
   payment badge is either hidden or a quiet "Cash". A card order moves `Authorizing → Authorized →
   Captured` alongside a lifecycle that is doing something completely different. Build both maps in
   `core/api/models/` now, even though nothing produces a non-`NotRequired` value until Milestone
   3.1 — retrofitting a second badge into a settled row layout is worse than reserving space for it.
3. **Order detail page:** items with `unitPrice`/`quantity`/`lineTotal`, **subtotal**, delivery
   address, notes, and a **status timeline** component — visual, mobile-friendly, built once and
   reused by the restaurant and driver views. Drive it from `ORDER_STATUS_META` and
   `toTimelineStatus()` (see [the two vocabularies](#the-two-order-status-vocabularies)). **Print a
   time only where the backend keeps one**: `placedOnUtc` on Placed, and `assignedOnUtc` /
   `pickedUpOnUtc` / `deliveredOnUtc` from `GET delivery/orders/{orderId}/delivery` on the driver
   steps. Accepted, Preparing and Ready get no time — `OrderResponse` has none (a nice-to-have in
   [backend prerequisites](#backend-prerequisites)). The Driver assigned step shows the driver's
   first name and last initial from the same delivery response. No call button: no DTO carries a
   phone number.
4. **Cancel order** where the state machine allows it — the design offers it in `Pending` and
   `Accepted` (verify against the Orders domain before shipping the copy; sheet 23 has the confirm
   text), via `POST orders/{id}/cancel`, with
   `app-modal` confirmation; surface the backend's rule violations as friendly messages.
5. **Polling refresh** (every ~15 s on the detail page) as a stopgap — explicitly replaced by
   SignalR in Phase 2. Keep the commit that deletes it; it's a good before/after story.

**New concepts:** mapping two independent enums onto one row, timeline/step rendering, polling with
`takeUntilDestroyed`, modeling "the server owns this rule" in the UI.

**💡 Hints**
- *Steps 1–3 (status metadata):* define **one** `ORDER_STATUS_META` keyed by the socket vocabulary
  and **one** `toTimelineStatus(rest: OrderStatus)` bridge, and drive the badge, the timeline and
  later the manager/driver views from them. When a status renders wrong anywhere, there is exactly
  one place to fix. Unit-test the bridge — nine socket values, eight REST values, and the
  `Pending`/`Placed` mismatch is the assertion that earns its keep.
- *Step 3 (rejected copy):* the manager's reject `reason` is not on `OrderResponse`, so the
  rejected banner (sheet 23) uses fixed copy, never "Reason: …".
- *Step 3 (timeline branches):* `Rejected` and `Cancelled` aren't steps on the line — give them
  `step: null` and render a terminal banner instead. `DriverAssigned` **is** a step, but it has no
  REST equivalent, so a page loaded fresh will never show it and a page that received the socket
  frame will. That's expected; don't "fix" it by faking a status.
- *Step 4 (cancel):* the backend's state machine is the source of truth — on failure show the
  ProblemDetails `detail` ("order already accepted"), don't replicate every rule client-side. Only
  hide the button in states where cancelling is *obviously* impossible (`Delivered`, `Cancelled`,
  `Rejected`).
- *Step 4 (409 is normal here):* an illegal transition is a `Conflict`, not a server error. Your
  interceptor should let 409 through to the page rather than toasting it as "something went wrong".
- *Step 5 (polling):* `interval(15_000).pipe(startWith(0), switchMap(() => api.getOrder(id)))`
  with `takeUntilDestroyed()` so navigation away stops it. `startWith(0)` makes the first load
  immediate — forgetting it means a 15-second blank page.
- *Step 5 (be kind to the limiter):* reads are the **first** thing the gateway's rate limiter sheds
  (`RateLimitTier.Read`). 15 seconds per open detail page is fine; 1 second is how you discover
  `Retry-After` the hard way.

**Done when:** placing an order and having the backend move it through states (via Scalar for now)
is fully visible in the customer UI, both badges render, and cancelling an accepted order shows the
backend's own explanation rather than a generic error.

### Milestone 1.5 — Restaurant Manager portal (4–6 days)

**What & why:** The other side of the marketplace, in the desktop shell. First heavy CRUD work —
where reactive forms really pay off.

> 🎨 **Design:** sheets 28–35 — incoming orders (28), arrival & authorising (29), loading/empty/error
> (30), reject modal (31), items & categories (32), item form (33), restaurant profile (34), order
> history (35).

**Steps**
1. **Incoming orders dashboard:** `GET orders` returns the manager's incoming orders, scoped from
   the token by owned restaurant (no parameter needed, and none available). Render `Pending` orders
   as prominent cards with their items, plus **accept** / **reject**. Three lanes (sheet 28): **New**
   (`Pending`), **In the kitchen** (`Accepted` and `Preparing` together, each card with a status chip
   and a "Start preparing" button while `Accepted`), and **Ready** ("Waiting for pickup", no button —
   the driver moves it on). Buttons follow the state machine:
   `POST orders/{id}/accept` → `POST orders/{id}/preparing` → `POST orders/{id}/ready`. Poll every
   ~10 s until Phase 2 real-time replaces it. If `OrderSummaryResponse` turns out not to carry item
   lines, each new card costs one `GET orders/{id}` — fetch it lazily per card.
   - **Reject modal (sheet 31):** a radio group of common reasons (out of stock, too busy, closing
     soon, other) plus an optional detail field, **composed into the single `{ reason }` string** the
     API takes. Tell the manager in the modal that the customer will not see the reason —
     `OrderResponse` does not carry it.
   - **Authorising card orders (sheet 29):** an arriving card order can sit in `Authorizing` for a
     moment. Show it greyed with "Payment authorising…" and poll `GET orders/{id}` **every 2 s**
     until `paymentStatus` is `Authorized` (enable Accept) or `Failed` (drop the card).
2. **Menu management:**
   - Category list with create (`POST .../menu-categories`) and rename.
   - **A reorder form built as a `FormArray`** — one `FormGroup` per category holding
     `{ categoryId, name, displayOrder }`, rendered as a list with up/down buttons that swap
     `displayOrder`, saved with one `PUT .../menu-categories/{categoryId}` per changed row. This is
     the `FormArray` exercise the old opening-hours editor was going to be, on an endpoint that
     exists.
   - Menu item create/edit in a drawer/modal form (sheet 33) (`categoryId`, `name`, `description`,
     `price`, `photoUrl`, `isAvailable`). On **edit**, category is shown read-only and availability
     is not in the form — `PUT .../menu-items/{id}` accepts neither; availability lives on the list
     toggle.
   - An **availability toggle** (`PATCH .../availability`) that flips items to sold out instantly
     from the list — the single most-used manager action, so it gets first-class UX.
3. **Restaurant profile page (sheet 34):** `PUT restaurants/{id}` — name, cuisine type (free
   text), email, phone, street, city, **postal code, country**, optional lat/lng. **Tax id is shown
   read-only — design decision**: the endpoint accepts it, but it is administrator-owned data set at
   onboarding, so the manager's form re-sends the loaded value unchanged. **No opening hours**: the
   field does not exist on `RestaurantResponse` and the endpoint accepts none.
4. **Order history (sheet 35):** the same `GET orders`, terminal statuses only, client-side. No
   filters and no totals exist on the endpoint, so the page is a table with **Prev / Next** only —
   never "Page 2 of 7".
5. Guard everything with `roleGuard('RestaurantManager')`; the backend enforces ownership — the
   frontend just handles 403s gracefully.

**New concepts:** CRUD-heavy forms, **`FormArray`**, edit-in-place UX, optimistic updates with
rollback on error (do it for the availability toggle only), handling authorization failures.

**💡 Hints**
- *Step 1 (dashboard):* reuse the polling recipe from 1.4 (10 s interval). Highlight orders that
  arrived since the last poll (compare ids, flash a ring) — cheap code, big "live" feel until
  real SignalR lands in 2.1.
- *Step 1 (accept can fail for a reason you won't expect):* once Milestone 3.1 exists, a card order
  whose authorization is still in flight returns `400 Orders.PaymentNotAuthorized` — *"The order
  cannot be accepted until its payment has been authorized"*. It is a **transient** state, typically
  under a second, and the right UI is a retry, not an error. Build the accept button so it can
  re-enable itself and say "payment still processing — try again", and you won't have to revisit it.
  Cash orders can never hit this (`PaymentStatus.NotRequired`).
- *Step 1 (why those four transitions and no more):* `OutForDelivery` and `Delivered` are driven by
  the Delivery service over the bus. There is no Orders endpoint for them and there should not be a
  button.
- *Step 2 (FormArray):* iterate `categories.controls` with `@for (…; track $index)` in the template;
  the `formArrayName` + index wiring is fiddly the first time — get **one** row rendering and saving
  before you style anything. Swap two rows by swapping their `displayOrder` values and marking both
  dirty, then `PUT` only the dirty ones.
- *Step 2 (no delete):* there is no `DELETE` for categories or items anywhere in the module. Don't
  build a delete button that calls nothing — mark items unavailable instead, and put the missing
  endpoint in your README's "known gaps" list (it is a nice-to-have in
  [backend prerequisites](#backend-prerequisites)).
- *Step 2 (item form):* one form component for both create and edit — pass an optional `item` input
  and `patchValue` when present. Price: `<input type="number" step="0.01">` plus a `> 0` validator;
  the value arrives as a number *or* string depending on browser — normalize in one place.
- *Step 2 (availability toggle):* the optimistic recipe — flip the signal immediately → fire the
  API call → on error flip back and toast. Do it for this one control only; everywhere else,
  boring "wait for the server" updates are the right default.
- *Step 2 (photos):* `photoUrl` is a URL and nothing else — there is no upload endpoint and no blob
  storage. A URL input with a live `<img>` preview and an `(error)` fallback is the whole feature.
- *Step 3 (commission is read-only here):* `commissionRate` is set at onboarding and
  `PUT restaurants/{id}` does not accept it. Show it, disabled, with a "set by the platform" hint.
- *Step 5 (403s):* the error interceptor should turn 403 into a "You don't have access to this"
  toast + redirect to the area home — build it once here, every later portal inherits it.

**Done when:** a manager can run their restaurant for a day without touching Scalar: see a new
order, accept it, progress it through preparing and ready, reorder their menu categories, and sell
out an item.

### Milestone 1.6 — Administrator portal (2–3 days)

> 🎨 **Design:** sheets 52 (restaurant step), 53 (manager step + confirmation), 54 (driver), 58
> (platform overview), all in the administrator desktop shell (sheet 10).

**Steps**
1. **Restaurant onboarding wizard** (2 steps): restaurant data (name, tax id, cuisine, full
   address, optional lat/lng, commission) → manager account (`managerEmail`, `managerFirstName`,
   `managerLastName`) → `POST restaurants` → success screen explaining the invitation email was
   sent. One call does both: the endpoint provisions the manager's invited account over the bus.
   The street field takes the house number too (there is no separate number field); cuisine is free
   text. The confirmation does **not** print an invitation expiry — the backend's lifetime is not
   exposed, so don't promise "seven days".
2. **Driver onboarding:** `POST delivery/drivers` with `{ email, firstName, lastName, vehicleType }`
   where `vehicleType` is the **name** `"Bicycle" | "Motorcycle" | "Car"`. Same
   provision-and-invite flow, different module. Returns the driver id, which is also the user id.
   The success screen links to `GET delivery/drivers/{id}` by that id — there is no driver list to
   return to.
3. **Support agent onboarding:** *blocked* — see [prerequisite #4](#backend-prerequisites). Build
   the form against the specified `POST users/invitations` shape so it is ready, but leave it behind
   a feature flag (or simply don't route to it) until the endpoint exists. Do **not** fake it by
   calling one of the other two. The form is sheet 54 without the vehicle field — reuse the
   component.
4. **Refund decisions** live here too, not in the support portal: `refunds:approve` is administrator
   only. Covered in Milestone 3.3 step 4, because the queue it decides on is a support screen —
   route it under `/admin` and link it from both.
5. **Platform overview (sheet 58)** — the admin landing page. Every tile is a count over a list
   that already exists: open refund requests (`GET support/refund-requests?status=Requested`),
   unassigned tickets (`GET support/tickets?unassigned=true`), plus the analytics summary. Lists are
   fetched with `pageSize=100` and shown as "100+" when full — there are no totals. **No orders or
   deliveries tiles**: those lists are unfiltered and would count only one page.
6. Now close the loop you built in 1.1: onboard a restaurant → find the activation link in the
   Notifications logs (or Seq at `:8081`) → activate in the SPA → log in as the new manager and see
   their empty restaurant. **This end-to-end flow across Identity, Users, Restaurants, email and the
   SPA is one of the strongest demos in the whole project.**

**💡 Hints**
- *Step 1 (wizard):* don't route between steps — one parent component with a `step = signal(1)`
  and one child form per step. Advance only when the current step's form group is valid
  (`markAllAsTouched()` on a failed "Next" so errors show). Keep both groups alive so "Back"
  preserves input.
- *Step 1 (commission — settled):* the backend wants a **fraction in [0, 1)**. `OnboardRestaurant`
  says so in a comment on the field: *"Fraction in [0, 1) — e.g. 0.20 = 20%."* So show a percentage
  input (`20`), validate **0 up to, but not including, 100**, and divide by 100 exactly once, in the
  API client. Sheet 52 adds a confirm when the typed value is below 1 ("0.2 % — did you mean
  20 %?"), the off-by-100 mistake an admin is most likely to make. Put a unit
  test on that conversion; an off-by-100 commission is the kind of bug that is invisible until
  someone reads a report.
- *Step 2 (vehicle type):* send the name, not the number — the request property is a `string`. The
  *response* (`DriverResponse.vehicleType`) comes back as `1 | 2 | 3`. Same enum, two encodings;
  this is the asymmetry from
  [What every JSON response looks like](#what-every-json-response-looks-like) biting for the first
  time.
- *Step 2 (no driver list):* there is no `GET delivery/drivers`. You can read one by id
  (`GET delivery/drivers/{id}`) if you already have it — the onboarding response gives you exactly
  that. Keep the ids you create in a session-scoped signal so the success screen can link to the
  profile you just made, and say in the README that a roster endpoint doesn't exist.
- *Step 6 (finding the email):* there is **no Mailpit** in `docker-compose.yml`.
  `EmailService.SendEmailAsync` logs the subject and body — activation link included — so read it
  from `docker compose logs fooddeliveryservice.notifications.api` or from Seq at
  `http://localhost:8081`. If the link points at `:3000` instead of your SPA, that is
  [prerequisite #3](#backend-prerequisites); fix the backend config, not the frontend.

**Done when:** you can onboard a fresh restaurant + manager and a fresh driver, and both can
activate and log in to their own portal — without touching the database or Scalar.

> ✅ **Phase 1 checkpoint:** tag a release, record a 2-minute demo GIF for the README, and take a
> breath. You have a full-stack marketplace: an admin onboards a restaurant, a manager fills in a
> menu, a customer orders from it in cash, and the manager cooks it. Everything after this is
> depth — it gets live, it gets paid, and it gets supported.

---

## Phase 2 — Real-Time & Driver Experience

> **Goal:** the app comes alive. Statuses update by themselves over a socket that already exists and
> already pushes five different frame types; drivers get a real mobile workflow built against the
> offer contract as shipped; and customers watch their food travel on a map. No polling survives
> this phase.

### Milestone 2.1 — SignalR foundation (2–3 days)

> 🎨 **Design:** sheet 12 (connection state — live / reconnecting / offline banner, and the replay
> rules), sheet 13 (the bell: which of the five frames produce a notification, per role), sheet 11
> (navigation is built from `GET users/me` roles, never from frames or permissions).

**Steps**
1. `RealtimeService` in `core/realtime/`: wraps one `HubConnection` to
   **`${environment.gatewayUrl}/hubs/tracking`** — through the gateway, on `:3000`, not to `:5600` —
   authenticated via `accessTokenFactory` (reusing `AuthService` tokens), with automatic reconnect
   and a connection-state signal (show a subtle "reconnecting…" indicator in the shell).
2. Start/stop the connection based on login state (`effect()` watching `isLoggedIn`).
3. Bridge the **five** hub methods to the app as signals or `Subject`s — `orderStatusChanged$`,
   `driverLocation$`, `deliveryOffered$`, `restaurantActivity$`, `supportActivity$` — typed to the
   frame records in [the API surface](#realtime--the-signalr-hub). **Register every `.on(...)`
   before `.start()`.**
4. **Implement the re-sync the backend assumes.** The hub's own contract says the client re-fetches
   authoritative state from `GET orders/{id}` and `GET delivery/orders/{orderId}/delivery` on
   connect and on every reconnect, then applies socket deltas. Wire `onreconnected` to re-fetch
   whatever the current screen is showing. This is not belt-and-braces; it is the reason the
   backend is allowed to persist nothing per frame.
5. Replace the polling from 1.4/1.5: the customer order detail and the restaurant dashboard now
   update instantly. Delete the polling code with a satisfied commit message.
6. **In-app notifications:** a bell icon in the shell with an unread badge and a dropdown/sheet
   listing recent events ("Your order was accepted", "Driver assigned: Marko"), fed by the same
   socket and kept in a signal-based `NotificationsService`. Sheet 13 lists which frames notify
   whom: `OrderStatusChanged` → customer, on Accepted, DriverAssigned, OutForDelivery, Delivered,
   Rejected, Cancelled; `RestaurantActivity` → manager, on a new "Placed" order (which also chimes)
   and on Cancelled; `SupportActivity` → agent, on Rejected and Cancelled only (the rest feeds sheet
   48). `DeliveryOffered` never rings — it takes the driver's whole screen — and
   `DriverLocationChanged` only moves the map. There are no ticket, refund or payment frames, so
   none of those can appear. **It is session-scoped and that is the
   only thing it can be**: the Notifications service has no HTTP endpoints — no list, no unread
   count, no mark-as-read. Closing the tab loses the list. Say so in the README; the alternative
   (persisting to `localStorage` and pretending it's server state) is worse, because it will
   disagree with a second device.

**New concepts:** WebSockets from the client, connection lifecycle management, push-based UI
updates, translating server events into signal updates, reconciling a best-effort stream with an
authoritative read model.

**💡 Hints**
- *Step 1 (connection):* the incantation is

  ```ts
  new HubConnectionBuilder()
    .withUrl(`${environment.gatewayUrl}/hubs/tracking`, {
      accessTokenFactory: () => this.auth.accessToken(),
    })
    .withAutomaticReconnect()
    .build();
  ```

  `accessTokenFactory` may return a `Promise<string>` — refresh there if the token is about to
  expire, otherwise a reconnect after a long idle will 401 against a 15-minute token.
- *Step 1 (why it works):* browsers can't set headers on a WebSocket connect, so SignalR passes the
  token as the `access_token` query parameter. The RealTime host already reads it —
  `AddRealTimeHubAuthentication()` hooks `OnMessageReceived` for `hubs/*` paths only — and the
  gateway's CORS policy sets `AllowCredentials: true` for exactly this. Both halves are done; if the
  handshake 401s, look at your token, not at the backend.
- *Step 1 (the limiter leaves you alone):* `hubs/**` is `RateLimitTier.Exempt` in
  `RateLimitRoutePolicy`. Negotiate, connect and the socket itself are never throttled.
- *Step 2 (there is nothing to join):* **the hub exposes no client→server methods.** Do not look for
  a `JoinOrderTracking`; there isn't one, and `invoke` will fail. Every group you belong to —
  `user:{yourUserId}` always, plus `restaurant:{id}` or `support` if your claims earn it — is
  derived server-side in `OnConnectedAsync` from your JWT, and re-derived on every reconnect. A
  customer automatically receives status and location frames for their own orders and for no one
  else's. This is simpler *and* safer than the join-by-id design; be ready to explain why.
- *Step 3:* register **all** `.on('EventName', handler)` listeners *before* calling `.start()` —
  events that arrive before a handler is registered are dropped silently, which looks exactly
  like "SignalR randomly doesn't work". Log every received frame to the console in dev.
- *Step 3 (status is a string):* `OrderStatusFrame.status` is one of the nine `OrderStatuses`
  strings, **not** the numeric REST enum. Feed it straight into `ORDER_STATUS_META`; feed REST
  responses through `toTimelineStatus()` first.
- *Step 5 & testing:* use one normal window + one incognito window for two different logins.
  Simulate flaky networks with devtools → Network → "Offline" and watch `withAutomaticReconnect`
  do its thing; the connection-state signal should visibly move disconnected → reconnecting →
  connected, and step 4's re-fetch should fire on the way back.
- *Step 6 (an admin sees less than you expect):* `support:dashboard` is seeded to SupportAgent
  only, so an administrator does **not** receive `SupportActivity`. An administrator *does* hold
  `restaurants:update`, so the hub tries to put them in a restaurant group, finds no manager replica
  row and logs a warning. Neither is a bug; both will look like one at 11pm.

**Done when:** two browser windows side by side (customer + manager): the manager clicks Accept and
the customer's timeline advances with no refresh. The connection banner from sheet 12 shows on a
dropped socket and clears on reconnect. Kill the network, bring it back, and the page
re-syncs to the truth rather than sitting on a stale frame.

### Milestone 2.2 — Driver portal (5–7 days)

**What & why:** The most mobile-critical part of the entire product — a driver uses this while
standing next to a scooter. Big touch targets, one primary action per screen, works one-handed.

> 🎨 **Design:** sheets 36 (home & availability), 37 (offer), 38 (active delivery), 39 (history &
> profile). Phone only — there is no desktop driver layout. Watch for the `#5` and `#6` tags:
> restaurant name/pickup address and cash-to-collect are **not** in today's DTOs.

**Steps**
1. **Driver home:** giant online/offline toggle calling
   `PATCH delivery/drivers/me/availability { available }`, current status from
   `GET delivery/drivers/me` (`DriverStatus`: `Offline | Available | Busy`), today's summary from
   `GET delivery/deliveries`. While online, send geolocation with
   `navigator.geolocation.watchPosition` → throttle to every few seconds →
   `POST delivery/drivers/me/location { latitude, longitude }`. Handle permission-denied with a
   clear explanation screen — **a driver who is `Available` but reporting no position is not an
   assignment candidate**, so this is not an optional nicety.
2. **The offer inbox — built against the contract that shipped.** The flow has two halves, and the
   socket is only the nudge:
   - **`GET delivery/drivers/me/offers`** is the authority. It returns
     `DeliveryOfferResponse[]` — `{ id, orderId, restaurantId, pickupLatitude, pickupLongitude,
     dropoffStreet, dropoffCity, dropoffPostalCode, dropoffCountry, dropoffNotes?,
     dropoffLatitude, dropoffLongitude, offerExpiresOnUtc, createdOnUtc }` — soonest deadline
     first, with lapsed offers already excluded server-side. An empty array means there is
     genuinely nothing to accept.
   - **The `DeliveryOffered` hub frame** carries only `{ deliveryId, orderId, offerExpiresOnUtc }`
     and its documented purpose is to tell the client that a call to the endpoint above is worth
     making. It deliberately carries no pickup or drop-off detail, so that a driver who was offline
     for the frame and a driver who received it **arrive at the same screen by the same call**.

   So: fetch the list on entering the portal; on a `DeliveryOffered` frame, re-fetch the list and
   match on `deliveryId`. Render the top offer as a full-screen card (sheet 37) — pickup point,
   destination, straight-line distances computed client-side from the coordinates, and a countdown to
   `offerExpiresOnUtc` — with **Accept**
   (`POST delivery/deliveries/{id}/accept`) and **Reject**
   (`POST delivery/deliveries/{id}/reject`). This is the most "app-like" screen in the project.
   **The pickup is coordinates and a `restaurantId` only** — a driver cannot call
   `GET restaurants/{id}` (`403`). Show "Pickup" plus distance until [prerequisite
   #5](#backend-prerequisites) adds the name and address.
   - **There is no retraction frame, ever.** An offer ends by lapsing, by being declined, or by
     being accepted, and only the last is something the realtime service hears about. The client
     **self-expires** on `offerExpiresOnUtc`; when the countdown hits zero, drop the card and
     re-fetch. The backend's consumer documents this explicitly, and it is the right design — a
     retraction frame that got dropped would strand a stale offer forever, so the deadline has to
     be load-bearing anyway.
   - **Losing the race is a `409`, not an error.** Two drivers can be offered in quick succession;
     accept is guarded by a distributed lock *and* by the aggregate. Render "that one's gone" and
     refresh the list.
3. **Active delivery flow:** one screen, one state machine mirroring `DeliveryStatus`:
   `Assigned` → navigate to restaurant → **Mark picked up** (`POST .../picked-up`) → navigate to
   customer → **Mark delivered** (`POST .../delivered`). Read the delivery with
   `GET delivery/deliveries/{id}` for the pickup coordinates and full drop-off address. Leaflet map
   with pickup/drop-off pins and the driver's own live position; a link out to Google Maps/Waze for
   actual navigation (what real driver apps do). **No call buttons** — no DTO carries a phone
   number. For cash orders the driver needs the amount to collect, which `DeliveryResponse` does not
   carry — [prerequisite #6](#backend-prerequisites); sheet 38 marks the spot.
4. **Delivery history list** from `GET delivery/deliveries` — self-scoped, paged, no filters. No
   earnings: the platform models a commission rate but never a driver payout, so don't display one.
   **Driver profile** on the same sheet (39): name and vehicle type are editable via
   `PUT delivery/drivers/me { firstName, lastName, vehicleType }` — the one profile in the app that
   *can* be saved. Email is read-only.
5. Test outdoors once with a phone on the local network (`ng serve --host 0.0.0.0`; geolocation
   needs HTTPS or localhost — use a dev tunnel, or accept emulated locations in dev).

**New concepts:** browser Geolocation API, permissions UX, Leaflet maps (markers, panning),
throttling high-frequency updates, designing for one-handed phone use, socket-as-hint /
REST-as-truth.

**💡 Hints**
- *Step 1 (geolocation):* `watchPosition` returns a watch id — store it and `clearWatch(id)` when
  going offline, or the phone keeps the GPS hot forever. Throttle sends with `throttleTime(3000)` —
  GPS can fire several times a second, and this is described in the endpoint's own doc comment as
  *"the system's highest-traffic endpoint"*.
- *Step 1 (it's fire-and-forget):* `POST .../location` bypasses the aggregate and the outbox
  deliberately — a position is telemetry, not domain state. It goes to a Redis geospatial set. So
  don't await it in a way that blocks the UI, and don't retry a failed one; the next tick is three
  seconds away.
- *Step 1 (simulating movement):* Chrome devtools → ⋮ → More tools → **Sensors** sets a fake
  location; for continuous movement, a dev-only "simulate route" button that emits interpolated
  coordinates on a timer beats fighting devtools.
- *Step 2 (countdown):* compute time-left from the **server-sent `offerExpiresOnUtc`** on every
  tick — don't count down from "when I received it". A backgrounded tab throttles timers, and your
  local countdown will drift from the backend's Quartz expiry job.
- *Step 2 (don't render from the frame):* it is tempting to build the offer card from
  `DeliveryOfferFrame` because it arrives first. Don't — it has three fields and none of them is an
  address. Use it as a trigger only. Writing that down in a code comment is a good interview
  anecdote about socket-as-hint design.
- *Step 3 (Leaflet setup):* install `leaflet` + `@types/leaflet`; add `leaflet/dist/leaflet.css`
  to the `styles` array in `angular.json` (missing CSS = gray tiles/broken layout). Create the
  map in `ngAfterViewInit`, never in the constructor, and call `map.invalidateSize()` if the map
  initializes inside a hidden/animating container — the "map renders as a gray square" bug is
  always one of these two.
- *Step 3 (marker icons):* Leaflet's default marker images 404 under bundlers — override
  `L.Icon.Default` with imported image URLs, or sidestep it entirely with `L.divIcon` + a
  Tailwind-styled div (a colored dot for the driver looks better anyway).
- *Step 3 (ownership is enforced, not advisory):* only the assigned driver can pick up or deliver;
  the domain refuses anyone else. You don't need a client-side check, just a sensible failure.
- *UX:* primary action buttons full-width at the bottom of the screen (thumb zone), min height
  ~56 px, one primary action per screen. Add `navigator.vibrate(200)` on a new offer — tiny API,
  delightful demo.

**Done when:** with the backend running and simulated movement, a driver can go online, receive an
offer (both via the socket and by cold-loading the offers list), accept it, pick up and deliver —
driving the customer's order all the way to `Delivered`, all touch-only. Let one offer expire on
screen and watch the card remove itself without a server frame.

### Milestone 2.3 — Customer live tracking map (2–3 days)

> 🎨 **Design:** sheet 24 — map with pickup, drop-off and driver pins, "last seen Ns ago" under the
> driver marker, step 6 of 7 on the timeline. **No ETA, no route line, no call button** — by
> decision.

**Steps**
1. On the customer order-detail page, once a delivery exists, call
   `GET delivery/orders/{orderId}/delivery` (customers hold `deliveries:read`, and the handler lets
   the order's own customer read it). Render a Leaflet map with the pickup point
   (`pickupLatitude/Longitude`), the drop-off (`dropoffLatitude/Longitude`) and the driver marker,
   moving on `DriverLocationChanged` frames.
2. **You do not join anything.** The frames arrive in your `user:{userId}` group automatically —
   the hub has no client→server methods. Filter incoming `DriverLocationFrame`s by `orderId` and
   ignore the rest. (If you read an older version of this plan that told you to
   `hub.invoke('JoinOrderTracking', …)` and `leave` it on destroy: that method never existed.)
3. Show the driver's name once assigned — from `DeliveryResponse.driverFirstName/LastName`, or from
   the `DriverAssigned` status frame's `driverName`/`driverVehicle`. Those are the only two sources:
   a customer does not hold `drivers:read` and cannot call `GET delivery/drivers/{id}`.
4. Handle the null cases honestly: `currentDriverLatitude/Longitude` are null before assignment,
   and null again once the delivery is terminal or the driver's position has gone stale. Render the
   two fixed pins and a "waiting for the driver" state rather than a map with one ghost marker.
5. Smooth the marker movement (simple interpolation between points — nice-to-have).

**💡 Hints**
- *Step 1 (reuse):* extract a shared `app-map` component from the driver work in 2.2 (inputs:
  markers, center; output: nothing) — two hand-rolled Leaflet setups will drift apart.
- *Step 1 (bounds):* call `map.fitBounds([...])` **once** with pickup + drop-off + driver, then
  stop touching the viewport — re-fitting on every location update makes the map lurch and users
  seasick. Offer a "re-center" button instead.
- *Step 2 (the `DriverAssigned` gotcha, again):* when the socket says `DriverAssigned`, the REST
  order is still `ReadyForPickup`. Your timeline will jump forward on the frame and jump back on
  the next `GET orders/{id}`. Fix it by treating the **delivery** as the source of truth for
  everything from assignment onward — `DeliveryStatus.Assigned = 2` is the real state — and let
  `ORDER_STATUS_META` render the merged view. Write the merge in one function and test it.
- *Step 5:* the simplest smoothing that looks good is a `requestAnimationFrame` lerp from the
  previous position to the new one over ~1 s. Plain `setLatLng` jumps are acceptable; smooth
  movement is the demo upgrade.
- **No ETA.** Feature 3.3 was never built and nothing on any DTO or frame carries an estimate.
  Don't design an empty slot for it — see
  [what this plan deliberately does not build](#what-this-plan-deliberately-does-not-build).

**Done when:** the full theater demo works: phone (driver, moving mock locations) + laptop
(customer) — the marker moves live, the driver's name appears, and the timeline agrees with itself
across a refresh. *This is the money shot for the portfolio README GIF.*

> ✅ **Phase 2 checkpoint:** no polling code remains *for anything a frame covers*. Three polls stay,
> on purpose, because no frame exists for them: `paymentStatus` while `Authorizing` (2 s, sheet 29),
> refund outcomes (5 s, sheet 57) and the ticket queue (30 s, sheet 40). Re-record the demo GIF with the live map in it,
> and add a short README section on the socket-as-hint / REST-as-truth design — it is the most
> architecturally interesting decision in the whole frontend, and you did not make it up, you read
> it off the backend's own contract.

---

## Phase 3 — Payments, Support & Production Polish

> **Goal:** the two shipped backend features this plan previously had no milestone for at all —
> Stripe payments (3.8) and Support & ticketing (3.6) — plus the quality pass that makes reviewers
> take the project seriously. Everything here exists server-side today; none of it is speculative.

### Milestone 3.1 — Payments: saved cards & paying by card (4–5 days)

**What & why:** Feature 3.8 is complete on the backend and entirely invisible without this
milestone. It is also the only place in the app where the *absence* of a thing (the card number) is
the architecture: Stripe Elements collects the card in the browser, against Stripe directly, and the
platform stays in PCI SAQ-A. One endpoint that accepted a card number would move the whole project
into a far heavier compliance tier — which is a genuinely excellent thing to be able to explain.

> 🎨 **Design:** sheet 25 (payment methods: one card, Replace, the pending row while the webhook
> lands), sheet 18 (Card at checkout — "Held now, charged when the restaurant accepts"), sheet 19
> (card declined).

**Steps**
1. **`PaymentsApi` + the saved-cards screen** (`/customer/payment-methods`): `GET
   payments/payment-methods` → `PaymentMethodResponse[]` (`brand`, `last4`, `expiryMonth`,
   `expiryYear`, `attachedOnUtc`). At most one card is saved at a time — saving another **replaces**
   it, so the UI is "your card", not "your cards", with a Replace button rather than an Add one.
2. **Add a card with Stripe.js.** Install `@stripe/stripe-js`. The flow is:
   1. `POST payments/payment-methods/setup-intents` → `{ setupIntentId, clientSecret }`.
   2. Mount a Stripe Elements card form and call `stripe.confirmCardSetup(clientSecret, …)`.
      The card goes from the browser to Stripe. **Your API never sees it.**
   3. Stripe returns success to the browser — **and the card is not saved yet.**
3. **Poll for the card, because the webhook is what saves it.** The endpoint's own description says
   so: *"This call is not the attachment: the card is saved when Stripe's `setup_intent.succeeded`
   webhook arrives, so a client polls `GET payments/payment-methods` rather than assuming success
   here."* Build an explicit pending row ("Adding your card… you can leave this screen") that polls
   with **backoff — 2 s, 4 s, 8 s, capped at 30 s — and gives up after two minutes** with "We didn't
   hear back from Stripe" and Try again (sheet 25). Replace is disabled while one is pending.
   **Do not optimistically render the card** — an optimistic card
   that never lands is a customer who thinks they can pay and can't.
4. **Card at checkout.** Go back to Milestone 1.3's checkout page and make the payment method a real
   choice: `"CashOnDelivery"` always, `"Card"` only when `GET payments/payment-methods` returns a
   card. Send the enum **name** in `POST orders`. **A decline arrives *after* `POST orders` has
   returned** — the order exists, then its `paymentStatus` goes `Failed` and it is cancelled. So for
   a card order, keep the cart until `paymentStatus` is `Authorized`; on `Failed`, show sheet 19 with
   the cart intact and two buttons — "Order again, pay cash" (primary) and "Use a different card".
   There is no "Retry payment": the declined order is already Cancelled, so either button places a
   new order.
5. **Make the payment badge real.** The `PaymentStatus` map you built in 1.4 now has something to
   show. On a card order it moves `Authorizing = 2` → `Authorized = 3` (funds held, nothing taken) →
   `Captured = 4` when the restaurant accepts, or `Released = 5` if the order is rejected or
   cancelled, or `Failed = 6` if the card is declined — in which case the order is cancelled
   alongside it. A cash order is `NotRequired = 1` forever. Render the pair on the order row and the
   order detail, and write one sentence of copy per state; "Authorized" and "Captured" mean nothing
   to a customer, "Card held" and "Card charged" do.
6. **Remove a card:** `DELETE payments/payment-methods/{id}` (204). Confirm with a modal, and say
   what it means — card payment disappears from checkout once Orders projects the event, which is
   *eventually*, not instantly. A brief window where checkout still offers Card is correct
   behaviour, not a bug; handle the resulting failure gracefully rather than trying to prevent it.
7. **Delete the scaffolding.** `POST payments/payment-methods/test-cards` exists only because this
   screen did not. Its doc comment says *"Delete it when the Angular card flow lands."* Raise that
   as a backend follow-up in the same PR — closing a loop someone else left open is a good habit and
   a good story.

**New concepts:** third-party JS SDK integration in Angular, an async-confirmation UX (poll, don't
assume), modelling two orthogonal status dimensions on one row, eventual consistency the user can
actually see.

**💡 Hints**
- *Step 2 (Stripe + Angular):* `loadStripe()` returns a promise — resolve it once in a service, not
  per component. Mount Elements in `ngAfterViewInit` against a `@ViewChild` element ref, and
  `element.destroy()` on destroy or a re-entered route leaks an iframe.
- *Step 2 (test cards):* `4242 4242 4242 4242` with any future expiry and any CVC always succeeds.
  The backend also names `pm_card_visa_chargeDeclined` and `pm_card_threeDSecure2Required` as the
  useful alternatives — use them through the dev test-card endpoint to exercise your failure and
  `Failed` states without fighting Elements.
- *Step 3 (why polling and not a socket):* the realtime hub has no payment frame. Five methods, none
  of them about money. Polling here is the right answer, not a stopgap — and knowing *why* the
  backend chose a webhook for this and a socket for order status is a genuinely good interview
  answer about which consistency each one needs.
- *Step 3 (webhooks in local dev):* Stripe cannot reach your laptop. Run the Stripe CLI's
  `stripe listen --forward-to localhost:3000/payments/webhooks/stripe` — the gateway route for it is
  anonymous and rate-limit exempt, precisely so this works.
- *Step 5 (the manager's side):* this is what makes Milestone 1.5's `Orders.PaymentNotAuthorized`
  retry real. Place a card order and hit Accept fast enough and you *will* see it; it is a real
  race, typically under a second, and the aggregate refuses rather than accepting an unpaid order.
- *Step 5 (`Released` vs `Refunded`):* a release is a hold that was never charged — it never appears
  on the customer's statement. A refund is money that moved and came back. The backend models them
  as different things because a customer's bank does; your copy should too.
- *Step 6 (don't guard what the server already guards):* a card id that isn't yours is a `404`, not
  a `403` — deliberately, so a 403 can't confirm that someone else's card exists. Treat 404 here as
  "already gone" and refresh.

**Done when:** a customer can save a card with Stripe Elements, see it appear only after the webhook
lands, place a card order, watch the payment badge go `Authorizing → Authorized → Captured` as the
restaurant accepts — and place a cash order in the same session with no payment badge at all.

### Milestone 3.2 — Customer support: my tickets (2–3 days)

**What & why:** Customers hold `support-tickets:open` and `support-tickets:read`. There is a real
customer-side support surface here, not just an agent portal, and the previous version of this plan
missed it entirely.

> 🎨 **Design:** sheet 26 (my tickets + thread) and **sheet 61 (open a ticket — new)**: pre-filled
> from an order, the category radiogroup, and the empty-submit error state.

**Steps**
1. **"Get help" entry point** on the order detail page and in the customer profile menu.
2. **Open a ticket (sheet 61):** `POST support/tickets { orderId?, subject, category }` → `Guid`. `orderId` is
   optional — not every ticket is about an order — but pre-fill it when the customer came from one.
   `category` is the enum **name**: `OrderNotReceived`, `ItemMissing`, `FoodQuality`, `DriverIssue`,
   `PaymentIssue`, `AppIssue`, `Other`. Render it as an accessible **radiogroup** (this is where the
   star-rating accessibility exercise went — see
   [what this plan deliberately does not build](#what-this-plan-deliberately-does-not-build)).
3. **My tickets list:** `GET support/tickets` — the same endpoint the agent queue uses, scoped to
   the caller. Show `reference` (the human-quotable id), subject, category, status, `openedOnUtc`.
   **`Escalated` is shown to the customer as "In progress"** (sheet 26) — internally it means "needs
   a senior agent"; to a customer it would read as "something went wrong".
4. **Ticket detail + thread:** `GET support/tickets/{id}` and `GET support/tickets/{id}/messages`.
   Render the conversation as chat bubbles, sided by `authorKind` (`Customer = 0`, `Agent = 1`,
   `System = 2`). Reply with `POST support/tickets/{id}/messages { body }` — omit `visibility` and
   it defaults to `CustomerVisible`, which is the only kind a customer may write anyway.
5. **Never render an internal note.** Agents write `InternalNote` messages to each other on the same
   thread. The backend filters them out **in SQL** for a customer caller, so a correct client simply
   never receives one — but build your bubble component so that a hypothetical `visibility === 1`
   message is *dropped*, not styled differently. Defence in depth costs one line here and is the
   single most consequential thing on this screen.

**New concepts:** chat-bubble rendering and auto-scroll, a pending-send state, accessible
radiogroups, "the server filters, and so do I".

**💡 Hints**
- *Step 4 (auto-scroll):* after appending a message set `container.scrollTop =
  container.scrollHeight` — but **only if the user was already near the bottom** (check before
  appending); yanking the view while someone reads an older message is the most common chat-UX bug.
- *Step 4 (no optimistic send — design decision):* only the menu availability toggle is optimistic
  in this app (sheet 60). Disable Send and show "Sending…" on the button, keep the text in the box,
  append the bubble when the `POST` returns its id. On failure the text is still in the box, with
  the error inline under it — nothing to reconcile, nothing to roll back.
- *Step 4 (author names):* `authorName` is **null for a customer-authored message** on purpose — the
  Support module keeps no customer-name replica, and the customer reading their own thread knows
  who they are. Render "You". It is also nullable for agents (a LEFT JOIN, so an agent whose
  registration event hasn't been projected yet still shows their message) — fall back to "Support".
- *Step 4 (input):* `<textarea rows="1">` that grows — set `height:auto` then `height:scrollHeight`
  on input, cap with `max-h-32`. Enter sends, Shift+Enter adds a newline.
- *Step 5 (no live updates):* there is no ticket frame on the SignalR hub. A customer sees an agent
  reply when they re-open the thread — and they get an **email**, which the backend sends on every
  customer-visible agent message. Poll on the open thread if you like (~20 s), and don't apologise
  for it in the README; email is the channel this feature was designed around.
- *Mobile:* the thread is `fixed inset-0` + `h-dvh` with the input pinned above the keyboard; test
  on a real phone — software keyboards eat fixed-bottom inputs (`interactive-widget` viewport meta
  and `dvh` units are the knobs to reach for).

**Done when:** a customer can open a ticket against a real order, see it in their list, reply on the
thread, and see an agent's reply — and an internal note written by that agent is nowhere in the DOM.

### Milestone 3.3 — Support agent & administrator portal (5–7 days)

**What & why:** Feature 3.6 shipped a substantial operational surface — a queue with claim/assign
under a distributed lock, an append-only audit trail, a two-person refund workflow and an analytics
summary — and the previous plan gave it one line. This is the data-dense desktop milestone.

> 🎨 **Design:** sheets 40–48 (queue, queue states, ticket detail, composer, audit trail, request
> refund, analytics, young dataset, live activity) and 55–57 (refund decisions, approve
> confirmation, refund outcomes). Tags `#7` (customer names), `#8` (restaurant names on the feed)
> and `#9` (agent roster) mark the gaps.

**Steps**
1. **Ticket queue** (`/support/tickets`): `GET support/tickets` with the real filters —
   `status`, `category`, `assignedAgentId`, `unassigned`, `from`, `to`, `page`, `pageSize`. The
   agent queue is `?status=Open&unassigned=true`. **This is the home for the URL-as-state and
   debounced-server-query exercise** that used to live on restaurant search: filters live in query
   params, a `debounceTime(300)` + `distinctUntilChanged()` + `switchMap()` pipe drives the request,
   and a shift lead can send "unassigned payment issues, last 7 days" as a link. **No search box** —
   the endpoint has no text parameter (sheet 40). Rows show the **customer id**, not a name, until
   [prerequisite #7](#backend-prerequisites). No frame announces a new ticket, so the queue
   re-reads **every 30 s and on window focus**, and new rows wait in a "3 new tickets" pill rather
   than inserting under the agent's focus ring. Pagination is Prev/Next — there is no total.
2. **Claim, assign, unassign:**
   - `POST support/tickets/{id}/claim` — no body, the agent is the caller.
   - `POST support/tickets/{id}/assign { agentId, reason? }` — naming *someone else* additionally
     needs `support-tickets:administer`, which only an administrator holds. `GET users/me` returns no
     permission list, so hide that control behind `hasRole('Administrator')` — the role is the only
     proxy you have for the code — and let the backend be the real gate. Agents get **Claim**
     only. Even for an administrator "Assign to…" has nobody to list until [prerequisite
     #9](#backend-prerequisites); render it disabled with that reason.
   - `POST support/tickets/{id}/unassign { reason }` — the **reason is required**; the aggregate
     refuses an empty one. Make it a required field in the modal, not an optional note.
   - All three take the same distributed lock key, so **losing a claim race is normal**: a `409`
     shows "another agent got there first" and **re-fetches the queue** (sheet 41), not just the
     row.
3. **Ticket detail** (two-pane on desktop, stacked below `lg:`):
   - Left: the same message thread from 3.2, **plus** an internal-note composer —
     `POST .../messages { body, visibility: "InternalNote" }`, gated on `support-tickets:manage`.
     Style notes unmistakably differently (a warning-toned left border and an explicit "Internal —
     the customer cannot see this" label). Getting this wrong is the worst bug this portal can have.
     The composer rules are on sheet 43 — see the step 3 hint, which this design changed.
   - Right: ticket metadata, `reference`, the customer id (`#7`), the order number as plain text
     (an agent holds no `orders:read`, so it cannot be a link), and the **status workflow** via
     `POST .../status { status, reason? }`. Statuses are `Open`, `InProgress`, `Resolved`,
     `Escalated`, `Closed`; the aggregate owns which moves are legal and an illegal one comes back
     as a 409 with a `detail` you should just show. `Resolved` needs a resolution note and
     `Escalated` needs a reason — both go in `reason`, as a required field that appears when that
     status is picked (sheet 42).
   - A second tab beside Conversation: the **audit trail** (sheet 44), `GET support/tickets/{id}/audit` — newest first, staff-only
     (`support-tickets:manage`). Render `action`, `actorName` (nullable — fall back to the id; there
     is no actor role on the entry, so don't print one),
     `fromValue → toValue`, `reason`, `occurredOnUtc` as a vertical timeline. It is append-only by
     design; there is no edit and no delete, and the UI should feel like a log, not a table you
     could change.
4. **Refunds — a two-person workflow, and the UI has to make that visible.**
   - An agent raises one from a ticket (sheet 45): `POST support/tickets/{id}/refund-requests
     { amount, reason }`. The `amount` is capped by the **replicated order subtotal**, the order and
     the customer are read from the ticket, and the reason is required **free text** — no reason
     chips, the API has no reason enum. Show the cap in the form.
   - The queue: `GET support/refund-requests?status=Requested` → `RefundRequestResponse[]`, which
     already joins `requestedByAgentName` and `decidedByAdminName` so the list reads as *"Jane
     asked, Sam approved"*.
   - The decision (sheets 55–56): `POST support/refund-requests/{id}/approve { note? }` or
     `/reject { note? }` —
     **administrator only** (`refunds:approve`), and **the aggregate refuses the requester even if
     they are an administrator**. So an admin looking at their own request must see the buttons
     disabled with "you raised this — another administrator must decide". Segregation of duties you
     can *see* is worth ten paragraphs of README.
   - **Two client-side rules stricter than the API — design decisions.** *Reject* requires a note
     (the body allows none, but an unexplained rejection is unauditable). *Approve* is not a yes/no
     dialog: the admin **re-types the whole-unit amount** ("Type 1440 to confirm") before Send
     enables (sheet 56); focus starts in that field, Enter does nothing until it matches, and the
     field never shows an error — only "matched" or "not yet". The request is single-flight.
   - **The decision is not the end.** `RefundStatus` runs `Requested = 0` → `Approved = 1` →
     `Settled = 3` **or** `Failed = 4`, asynchronously, once the Payments service acts on it.
     `settledOnUtc`, `failedOnUtc` and a bounded `failureReason` are all on the response. An
     approved-but-not-yet-settled row is the normal case for a second or two; a `Failed` row with
     `failureReason` is actionable (a cash order needs settling by hand, an over-large amount needs
     a smaller request) and should render as a call to action, not an alarm.
   - **Outcomes (sheet 57).** No frame carries refund outcomes, so while any row is `Approved` the
     tab re-reads the list **every 5 s** and updates rows in place. A row still `Approved` after a
     few minutes (sheet 57 shows 4) says "Taking longer than usual — check the payment provider's
     dashboard"; nothing in the app can chase it. A cash order's refund always fails
     (`failureReason`: settle manually) — render that reason verbatim.
5. **Analytics summary** (`/support/analytics`): `GET support/analytics/summary?from&to`. The
   response echoes the window it computed (`fromUtc`/`toUtc`) so your chart can label its own axis.
   Render:
   - Headline tiles from `totals`: `ticketsOpened`, `ticketsResolved`, `ticketsFirstResponded`, and
     the four durations. **Every duration is nullable** — a window in which nothing was resolved has
     no resolution time, and rendering `0` there says "instant" when the truth is "empty". Show "—".
   - A bar chart from `ticketsPerDay` (`{ date, opened, resolved }`). It is **gap-filled
     server-side**: a quiet day is a row of zeroes, not a missing row, specifically so a chart
     doesn't draw a straight line across it. Don't filter those rows out.
   - Simple breakdowns from `byCategory`, `byStatus`, `byAgent` (`agentName` nullable again).
   - `refunds` as `{ status, count, totalAmount }` — read it with the status beside it, since
     `Approved`, `Settled` and `Failed` mean three different things about where the money is.
   - **Comparison figures** ("vs 138 in the previous 30 days") are a **second call** with the window
     shifted back — there is no comparison field. The delta arrow is grey, never green/red.
   - **Young dataset (sheet 47):** show a mean only with **at least 5 samples** ("an average needs at
     least 5"), median-only below that, and draw gap-filled zero days on the baseline.
6. **Live activity (sheet 48, agents only).** Render `SupportActivity` frames as a session-scoped,
   status-only feed — `{ orderId, restaurantId, status, occurredOnUtc }`; no from→to transition,
   no actor. Restaurants appear as ids until [prerequisite #8](#backend-prerequisites).
   Administrators are not in the `support` group, so the admin shell has no such tab.

**New concepts:** data-dense desktop tables (sorting/filtering/pagination as reusable patterns),
URL-as-state for real, multi-pane layouts, permission-driven UI, simple data visualization, rendering
an async decision outcome.

**💡 Hints**
- *Step 1 (tables):* resist building a generic `<app-table>` — it's a classic rabbit hole. A plain
  `<table>` per page with shared Tailwind classes and small reusable pieces (pagination bar,
  sort-header component) gets you everything with a tenth of the complexity. Wrap tables in
  `overflow-x-auto` so they survive narrow screens.
- *Step 1 (filter values are names):* `status` and `category` go on the query string as enum
  **member names** (`?status=Open&category=FoodQuality`), while the *responses* carry numbers. Same
  asymmetry as everywhere else.
- *Step 2 (permission-driven UI is a courtesy, not a control):* hide what the user can't do so the
  screen isn't a minefield, but let the 403 be the real answer. Your interceptor already handles it
  from Milestone 1.5.
- *Step 3 (composer mode — changed by the design, sheet 43):* every ticket **opens in "Reply to
  customer"**; the mode is never carried across tickets. *Within* a ticket it is sticky: after
  saving an internal note the composer stays internal, because agents write two or three in a row
  and a toggle that flips back every time is the one that eventually gets ignored. Switching mode
  repaints the whole composer (border, fill, label, placeholder, button colour and words) and is
  announced to screen readers. Drafts are kept per ticket *and* per mode. `⌘↵` sends in whichever
  mode is active, and there is deliberately no shortcut that switches mode — a single keystroke must
  never change where text goes. The endpoint defaults `visibility` to customer-visible; always send
  it explicitly anyway.
- *Step 3 (`support-analytics` and `support-tickets:manage` are agent-held; `refunds:approve` is
  not):* an agent sees the whole portal except the approve/reject buttons. Build it as one feature
  area with permission-gated controls, not two portals.
- *Steps 4/5 (charts):* a bar chart is `flex items-end` + divs with `height: (value/max)*100%` +
  tooltips via `title` — genuinely enough here. If you want a library, Chart.js is the boring-good
  choice; by this milestone you can afford it.
- *Step 5 (median beside mean):* the response gives you both, deliberately — one week-old ticket
  drags a mean far enough to hide what the typical customer experienced. Show both; it is the same
  instinct as the load-test report leading with p95, and saying that out loud is a good interview
  moment.
- *An agent cannot open a ticket.* `POST support/tickets` needs `support-tickets:open`, seeded to
  Customer and Administrator only. The endpoint's `onBehalfOfCustomerId` field is therefore an
  admin affordance. Don't put "New ticket" on the agent toolbar.

**Done when:** an agent can work a ticket end to end — claim it from the queue, reply, leave an
internal note the customer's session never renders, request a refund — a *different* administrator
approves it, the row moves to `Settled`, and the analytics summary reflects the day's work.

### Milestone 3.4 — Production polish (4–6 days, spread out)

The checklist that separates "student project" from "hire this person".

1. **PWA:** add `@angular/pwa` — installable on a phone home screen with an icon and offline app
   shell. For a food-delivery app this is *the* fitting finishing touch, and it's cheap. (Web push
   is **not** part of this — see
   [what this plan deliberately does not build](#what-this-plan-deliberately-does-not-build).)
2. **Accessibility pass:** keyboard-navigate every flow; labels on all inputs; focus trap in
   modals; `alt` texts; check color contrast of your tokens; run a Lighthouse a11y audit ≥ 95.
3. **Performance pass:** run Lighthouse on the customer area (mobile preset); check lazy chunks are
   sensible (`ng build` bundle stats); add `@defer` for below-the-fold heavy bits (the map, the
   audit trail); `NgOptimizedImage` for menu photos.
4. **Error & edge polish (sheets 59–60):** offline banner (`navigator.onLine`), 404 page, empty
   states everywhere, a form double-submit audit, and a **429 story** — the gateway's edge limiter
   really will shed reads under load. Sheet 59 is the taxonomy — one payload, four surfaces:
   **field-level** for a 400 with `errors`; **inline at the form** for a 400/409 with no field named
   (branch on the `title` code, render `detail` verbatim); **full page + retry** when a 403 covers a
   whole area or a 404/5xx leaves the screen nothing to show; **toast** for a 403 on one action or
   any failed background action while the page still works. 401 → sign in again. 429 shows an **inline countdown from `Retry-After` and retries
   automatically** — **except at checkout**, where it never auto-retries a `POST orders`. Sheet 60
   is the loading side: skeletons on first load only, button spinners for actions, and the
   availability toggle as the single optimistic control. `docs/rate-limiting.md` has the tiers if
   you want the numbers.
5. **Browser ↔ backend correlation.** The replacement for the old Application Insights item, and a
   better one: the gateway's CORS policy already exposes `X-Correlation-Id` to the browser by name
   (`EdgeCorsOptions.ExposedHeaders`). Surface it in your error toasts and as a "Reference: …" line
   on every full-page error (sheet 59), log it to the console in dev, then paste it into Seq (`:8081`) or Jaeger (`:16686`) and read the whole server-side trace
   for that one click. End-to-end correlation from a button to a SQL query, in about ten lines and
   with no SDK. Write the README paragraph; it is a spectacular interview demo.
6. **E2E suite:** 3–5 Playwright tests — login, browse + order happy path, manager accept, guard
   redirect, and one cross-role test (manager accepts → customer's socket advances). Wire into the
   frontend CI job.
7. **Deploy — and be honest about what "deployed" means here.** Backend Feature 2.5 was **scoped
   down**: what exists is plain `kubectl` manifests in `Backend/deploy/` for a local KinD cluster.
   Helm, HPA, Ingress, CI-deploy and AKS were all cut, so **there is no hosted backend to point a
   hosted SPA at.** Two honest options:
   - Build and deploy the SPA to Azure Static Web Apps (free tier) with the backend base URLs
     pointing at a locally-run stack, and say in the README that the live link is the frontend only.
   - Or skip hosting entirely and ship a `docker compose up` + `ng serve` quickstart plus GIFs.

   Either way, add the README section: architecture diagram including the frontend, screenshots and
   GIFs (the tracking map!), and a clear-eyed "what runs where" note. A portfolio that explains its
   own deployment boundary reads as more senior than one with a dead demo link.
8. **(Optional stretch)** refactor `CartService` to `@ngrx/signals` SignalStore and write one
   paragraph in the README comparing the two — interview gold.

**💡 Hints**
- *Step 1 (PWA):* the service worker is **disabled in `ng serve`** — test with a production build:
  `ng build` then serve `dist/` with `npx http-server`. While developing, keep devtools →
  Application → Service Workers → "Update on reload" checked, or you'll spend an afternoon
  debugging a stale cached bundle (everyone does this once).
- *Step 2 (a11y):* fastest wins first — every `app-input` already has a label (0.B pays off),
  `alt` on images, visible focus rings (don't remove Tailwind's defaults), Escape closes modals
  (free with `<dialog>`). Then run Lighthouse and fix what it lists; it names exact elements.
- *Step 3 (bundles):* `ng build` prints per-chunk sizes; investigate with `npx esbuild-visualizer`
  or source-map-explorer if a chunk balloons. Likely suspects: Leaflet and `@stripe/stripe-js`
  imported eagerly — confirm both live only in lazy chunks, and wrap the map in `@defer (on
  viewport)`.
- *Step 6 (Playwright):* use the `webServer` option in `playwright.config.ts` so tests auto-start
  `ng serve`; select elements by role/label (`getByRole('button', { name: 'Place order' })`) —
  resort to `data-testid` only when that fails. Seed a dedicated test user; never depend on state
  a previous test created.
- *Step 7 (SPA deploy):* deep links 404 on static hosts until you add the SPA fallback — for
  Azure Static Web Apps that's `staticwebapp.config.json` with `navigationFallback` →
  `/index.html`. Swap `environment.ts` URLs at build time via the production file replacement, not
  by hand.

> ✅ **Phase 3 checkpoint — and the real definition of done for this plan:** every screen in the app
> is backed by an endpoint or a hub frame that exists, the README's "known gaps" section lists the
> nine backend prerequisites and the nice-to-haves by name, every "needs backend" spot on the canvas
> renders its fallback, and nothing on screen is a placeholder for a feature that was never built.

---

## Conventions & Working Rules

Small set — consistency beats cleverness:

- **Components:** standalone; `changeDetection: ChangeDetectionStrategy.OnPush` everywhere
  (signals make this free); templates use `@if`/`@for`; inputs/outputs via `input()`/`output()`
  functions; inject dependencies with `inject()`.
- **Naming:** files `kebab-case` (CLI default); one component per file; page components end in
  `*Page` (`OrderDetailPage`), presentational ones don't.
- **DTOs:** TypeScript `interface`s in `core/api/models/`, named exactly like the backend
  responses. **Enums are numeric on the way in and string names on the way out** — model both, in
  one file per backend module, and never inline a magic `1`. Dates arrive as ISO strings — keep
  them as strings in DTOs and convert at the edge (pipes); it's a classic beginner trap.
- **No `any`.** If you're tempted, the DTO is missing.
- **Two base URLs.** `gatewayUrl` and `identityUrl`, both from `environment.ts`. A third one in a
  component is a code-review reject.
- **Never guess at a contract.** If a field or endpoint isn't in
  [The API surface as it exists today](#the-api-surface-as-it-exists-today) or in
  `http://localhost:3000/docs/{slug}/scalar`, it doesn't exist — raise it as a backend prerequisite
  with a shape instead of designing around its absence. That rule is why this revision was needed.
- **Tailwind:** mobile-first always (base classes = phone, `md:` adds desktop); extract repeated
  class clusters into a shared component rather than `@apply`.
- **Git:** same discipline as the backend — feature branches, conventional-ish commits
  (`feat(customer): cart drawer`), PRs even solo (CI must pass).
- **One `TODO(frontend-plan)` grep before each phase checkpoint** — no silent leftovers.

## Testing Strategy

Pragmatic pyramid — enough to prove the skill, not test theater:

| Layer | Tool | What to cover |
|---|---|---|
| Unit | Vitest | The logic-bearing things: `CartService` (subtotal, one-restaurant rule), `AuthService` (refresh decision, single-flight), the **`toTimelineStatus()` bridge** (all nine socket values, all eight REST values, and the `Pending`→`Placed` case), the **commission percent↔fraction conversion**, the payment-status map, pipes. Aim for *meaningful* tests, not coverage %. |
| Component | Vitest + Angular testing utilities | A handful: login form validation display, the status timeline per status, the ticket thread **dropping an `InternalNote`**, the offer card self-expiring on `offerExpiresOnUtc`. |
| E2E | Playwright | 3–5 happy paths (Milestone 3.4). |

The three unit tests in bold are the ones that pay for themselves. Each guards a mismatch between
two systems — two status vocabularies, two ways of writing a percentage, two audiences for one
message thread — and every one of them is a bug that ships silently and is found by a customer.

Write unit tests *with* each milestone (the backend habit transfers directly); leave component
tests for when a bug bites or a component stabilizes.

## Learning with AI Tools

You'll build this with AI assistance — use it to learn, not to skip learning:

1. **Type the code yourself for new concepts.** First interceptor, first guard, first signal
   store: ask the AI to *explain*, then write it. Copy-paste only what you've already built once.
2. **Ask for reviews, not solutions:** "here's my CartService — critique it like a senior Angular
   dev" teaches more than "write me a CartService".
3. **When stuck > 30 min**, ask for a hint ("what concept am I missing?") before asking for the fix.
4. **Interrogate everything you paste:** "why `switchMap` and not `mergeMap` here?" — interviewers
   ask exactly these questions.
5. **Never let it invent an endpoint.** An assistant asked to "add restaurant search" will happily
   write `GET restaurants?q=` because every other food app has one. Check every route it gives you
   against `docs/{slug}/scalar` or the `IEndpoint` class. **This whole revision exists because a
   plan was written against a project plan instead of against the code** — the same mistake is one
   confident paragraph away at any moment.
6. Keep a `LEARNING_NOTES.md` — one line per "aha". It becomes your interview prep doc for the
   frontend side, feeding the same interview-questions docs you keep for the backend.

---

*Execute incrementally. Phase 0 + Phase 1 alone produce a demo-able full-stack product; each later
phase adds visible wow (live maps, real card payments, an operational support desk) on top of a
solid foundation. Keep the backend the star of the show — the frontend's job is to make it
undeniable, and to make it undeniable **as it actually is**.*
