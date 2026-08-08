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

---

## Table of Contents

- [Part 1 — High-Level Overview](#part-1--high-level-overview)
  - [What we are building](#what-we-are-building)
  - [Technology stack](#technology-stack)
  - [Architecture](#architecture)
  - [Project structure](#project-structure)
  - [How the frontend talks to the backend](#how-the-frontend-talks-to-the-backend)
  - [Backend prerequisites](#backend-prerequisites-small-tasks-on-the-net-side)
  - [Coverage matrix — every backend feature mapped](#coverage-matrix--every-backend-feature-mapped)
- [Part 2 — Detailed Implementation Plan](#part-2--detailed-implementation-plan)
  - [Phase 0 — Foundations](#phase-0--foundations)
  - [Phase 1 — Core Features](#phase-1--core-features-matches-backend-phase-1)
  - [Phase 2 — Real-Time & Driver Experience](#phase-2--real-time--driver-experience-matches-backend-phase-2)
  - [Phase 3 — Support, AI & Production Polish](#phase-3--support-ai--production-polish-matches-backend-phase-3)
- [Conventions & Working Rules](#conventions--working-rules)
- [Testing Strategy](#testing-strategy)
- [Learning with AI Tools](#learning-with-ai-tools)

---

# Part 1 — High-Level Overview

## What we are building

**One single-page application (SPA)** with five role-based areas, not five separate apps:

| Area | Users | Primary device | Key screens |
|---|---|---|---|
| **Customer** | Customers | 📱 Phone | Browse restaurants, menu, cart, checkout, live order tracking with driver on a map, order history, reviews, AI support chat |
| **Restaurant** | Restaurant Managers | 💻 Tablet/desktop | Live incoming-orders dashboard, accept/reject, order preparation flow, menu & category management, opening hours, reviews received |
| **Driver** | Delivery Drivers | 📱 Phone (always) | Go online/offline, receive assignment, accept/reject, navigate to pickup, mark picked-up/delivered, delivery history |
| **Admin** | Administrators | 💻 Desktop | Onboard restaurants, provision staff/partner accounts (invitations), platform overview |
| **Support** | Support Agents | 💻 Desktop | Ticket queue, ticket detail with order history + chatbot transcript, refund requests, fraud flags |

**Why one app instead of several?** One app is simpler to build, test, and deploy; role-based lazy
loading means a driver's phone never downloads the admin screens; and it demonstrates the
job-relevant skills (route guards, lazy loading, role-based UI) better than duplicating
boilerplate across repos. Real companies with separate apps still structure each one exactly like
one of our role areas — so nothing is lost for learning.

**Mobile-first is non-negotiable.** Every customer and driver screen is designed for a ~375 px
wide phone screen first, then progressively enhanced for tablet/desktop with Tailwind's responsive
prefixes (`sm:`, `md:`, `lg:`). Restaurant/admin/support screens are desktop-first but must remain
usable on a tablet.

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
| **RxJS (targeted use)** | Async streams | Only where it genuinely fits: HTTP calls, debounced search input, SignalR event streams. You do not need to be an RxJS wizard — knowing `map`, `switchMap`, `debounceTime`, `catchError` covers this project. |
| **Angular Reactive Forms (typed)** | Forms | Login, registration, checkout, menu editing. The typed-forms API is the standard answer to "how do you handle forms in Angular?" in interviews. |
| **@microsoft/signalr** | Real-time | Official SignalR JavaScript client — connects to the backend's realtime service for live order status and driver location. |
| **Leaflet + OpenStreetMap** | Maps | Free, no API key, tiny learning curve. Displays the driver's live position for customers and the delivery route for drivers. (Google Maps needs billing setup; Leaflet is the standard free choice.) |
| **angular-eslint + Prettier** | Code quality | Linting and formatting exactly like `dotnet format` + analyzers. Set up once, forget. |
| **Vitest** | Unit tests | Angular's current default test runner (replaces Karma). Fast, simple API. |
| **Playwright (small suite)** | E2E tests | 3–5 happy-path browser tests. Even a tiny E2E suite is a strong CV signal. |
| **GitHub Actions** | CI/CD | Build + lint + test on every push, mirroring the backend pipeline. Deploy to Azure Static Web Apps later. |

**Deliberately NOT used (and why):**

- **NgRx Store** — the classic Redux-style state library. Very common in legacy enterprise job
  postings, but overkill for this app and a steep learning curve. Signals in services cover our
  needs. *Optional stretch goal:* refactor ONE feature (the cart) to `@ngrx/signals` SignalStore
  at the end, so you can honestly discuss it in interviews.
- **Angular Material / PrimeNG** — component libraries would fight Tailwind and hide the CSS
  learning. We build a small set of our own UI components instead (great learning, great
  portfolio evidence). If a screen needs something genuinely hard (date picker), reconsider then.
- **Server-side rendering (Angular SSR)** — meaningful for SEO/marketing pages; our app is behind
  a login. Skip.
- **Micro frontends, module federation, monorepo tooling (Nx)** — real technologies, wrong
  project size. Mentioning *why* you didn't use them is itself a good interview answer.

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
└───────┬──────────────────────┬──────────────────────────┬─────────────────┘
        │ REST (JSON)          │ tokens                   │ WebSocket
        ▼                      ▼                          ▼
  YARP Gateway :3000     Identity :18080           Realtime :5600
  users/** orders/**     POST /connect/token       SignalR hubs/**
  restaurants/**         (login + refresh)
  delivery/** ...
```

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

5. **One typed API client service per backend module** (`OrdersApi`, `RestaurantsApi`, …), each a
   thin wrapper over `HttpClient` returning typed DTOs that mirror the backend contracts. All
   backend URLs come from `environment.ts` — never hard-coded in components.

6. **Errors follow the backend's `ProblemDetails` format.** One HTTP interceptor converts every
   failed response into a typed `ApiError`, shows a toast for unexpected errors, and lets
   validation errors flow to forms. (The backend's Railway-Oriented `Result` failures arrive as
   RFC 7807 problem+json — the frontend should treat that contract as seriously as the backend does.)

### Authentication design (matches the existing backend exactly)

The backend's Duende IdentityServer already has a **public client with the resource-owner-password
grant and refresh tokens enabled** (`fooddeliveryservice-public-client`). That means the frontend
does **not** need an OIDC redirect library — login is a plain form POST:

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

Frontend responsibilities:

- **`AuthService`** — logs in, stores tokens, exposes `currentUser` / `isLoggedIn` / `roles` as
  signals, schedules/executes refresh, logs out.
- **Auth interceptor** — attaches `Authorization: Bearer …` to every API request; on a 401,
  attempts one token refresh and replays the request; if refresh fails, redirects to login.
- **Route guards** — `authGuard` (must be logged in) and `roleGuard('Customer')` etc. per area.
  After login, the app routes to the user's home area based on their role(s); users with multiple
  roles get a simple area switcher.
- **Token storage:** `localStorage`, with an honest README note about the XSS trade-off and what
  production would do differently (BFF/cookie pattern). Interviewers love that you know the
  trade-off; the pragmatic choice is fine for a portfolio SPA.

> ⚠️ ROPC (password grant) is deprecated in OAuth 2.1 — the backend chose it deliberately for
> simplicity. Be ready to say in interviews: "production would use Authorization Code + PKCE with
> redirect to the identity provider; my backend exposes ROPC so the SPA posts credentials
> directly." Knowing *why* is worth more than the fancier flow.

---

## Project structure

The frontend lives in `Frontend/` next to `Backend/` in the same monorepo (mirrors Feature 1.1 of
the backend plan).

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
    │   │   │   ├── ui/               #   button, input, card, badge, spinner, modal, toast, empty-state
    │   │   │   └── pipes/            #   money pipe, relative-time pipe, order-status pipe
    │   │   ├── features/
    │   │   │   ├── auth/             #   login, register, activate-invitation pages
    │   │   │   ├── customer/         #   restaurants, menu, cart, checkout, orders, tracking, reviews, chat
    │   │   │   ├── restaurant/       #   dashboard, orders, menu-editor, profile
    │   │   │   ├── driver/           #   home (online/offline), assignment, active delivery, history
    │   │   │   ├── admin/            #   restaurant onboarding, account provisioning
    │   │   │   └── support/          #   tickets, ticket detail, fraud flags (Phase 3)
    │   │   ├── app.routes.ts         # top-level routes; each feature lazy-loaded
    │   │   ├── app.config.ts         # providers: router, http+interceptors, etc.
    │   │   └── app.component.ts
    │   ├── environments/             # environment.ts / environment.development.ts (API URLs)
    │   └── styles.css                # Tailwind import + design tokens (CSS variables)
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
| **YARP Gateway** | `http://localhost:3000` | ALL REST calls: `users/**`, `orders/**`, `restaurants/**`, `delivery/**`, `notifications/**` |
| **Identity (Duende)** | `http://localhost:18080` | `POST /connect/token` (login + refresh); invitation activation |
| **Realtime (SignalR)** | `http://localhost:5600` | `hubs/**` — live order status + driver location (Phase 2, per `REALTIME_PHASE2_PLAN.md`) |

Only these three base URLs exist in `environment.ts`. Individual services (Orders :5200 etc.) are
an internal backend detail the frontend never sees — exactly the point of the gateway.

---

## Backend prerequisites (small tasks on the .NET side)

Do these on the backend **before** frontend Phase 1; each is small:

1. **CORS** — the SPA (e.g. `http://localhost:4200`) is a different origin. Add a CORS policy
   allowing the SPA origin on the **Gateway** and the **Identity** host (and later the Realtime
   host, which additionally needs `AllowCredentials` for SignalR WebSocket negotiation).
2. **Roles visible to the client** — the app routes users by role after login. Verify the access
   token (or a `users/profile` response) exposes the user's roles; if not, add role claims to the
   Duende profile service or extend the profile endpoint.
3. **Invitation activation route** — the activation email should link to the SPA
   (`/auth/activate?token=…&email=…`), and the SPA posts to the set-password endpoint. Confirm
   that endpoint is reachable for an anonymous caller (directly on Identity or via a gateway
   route) and that the email template contains the SPA URL.
4. **Swagger check** — the frontend DTOs will be hand-written from the Swagger/Scalar docs of each
   service. Make sure they're accurate (backend Feature 3.7 cares about this anyway).

---

## Coverage matrix — every backend feature mapped

The frontend plan covers **all three phases** of `FoodDelivery_ProjectPlan.md`. This table maps
each backend feature to the milestone(s) where its UI lives, so nothing silently falls through.
Some backend features are pure infrastructure with no screens — those are marked and briefly
justified, which is itself a fact worth knowing at demo time.

| Backend feature | Frontend coverage |
|---|---|
| **1.1** Solution structure | `Frontend/` folder in the same monorepo; CI for `Frontend/**` — **Milestone 0.A** |
| **1.2** Identity (registration, login, invitations, refresh, logout) | Login, customer registration, invitation activation, silent refresh, logout, session restore — **Milestone 1.1** |
| **1.3** API Gateway | All REST traffic goes through the gateway :3000; frontend never sees internal services — **all milestones**; 401/403 handling in interceptors (**1.1**) |
| **1.4** Restaurant Service | Customer browse/search/filters (cuisine, rating, proximity "near me") + menus — **1.2**; manager menu/category CRUD, availability, profile & opening hours — **1.5**; admin onboarding — **1.6** |
| **1.5** Order Service | Cart, checkout with idempotency key — **1.3**; order list/detail/timeline/cancel — **1.4**; restaurant accept/reject/status flow — **1.5** |
| **1.6** Notification Service | Emails are backend-side; the SPA's surfaces are the invitation-activation link target (**1.1**) and the in-app notification bell + toasts fed by SignalR (**2.1**); PWA push notifications — optional stretch in **3.4** |
| **1.7** CI/CD Phase 1 | Frontend CI from day one — **0.A**; deploy to Azure Static Web Apps — **3.4** |
| **2.1** Delivery Service & drivers | Full driver portal: availability, geolocation updates, assignment offers with expiry, picked-up/delivered flow, history — **2.2** |
| **2.2** SignalR real-time | Realtime connection service, live order status for customer + manager dashboards — **2.1**; live driver map for customers — **2.3** |
| **2.3** Redis caching | Backend-only (no UI); the frontend just gets faster responses. Worth one README sentence, zero screens |
| **2.4** Telemetry & observability | Mostly backend; optional frontend contribution: Application Insights **JavaScript SDK** for browser-side page/AJAX telemetry correlated with backend traces — optional item in **3.4** |
| **2.5** Kubernetes / AKS | Backend-only; frontend deploys as static assets (**3.4**) and is unaffected by cluster topology |
| **2.6** Reviews & ratings | Review submission, star component, ratings in search/detail, manager read-only view — **2.4**; support-agent **moderation** (hide abusive reviews) — **3.1** |
| **3.1** AI support chatbot (RAG) | Full chat UI with escalation-to-human state — **3.2**; the pre-escalation transcript shown to agents — **3.1** |
| **3.2** Personalised recommendations | "Recommended for you" + "Trending near you" on customer home — **3.3** |
| **3.3** AI-powered ETA | Live ETA on checkout, order detail, tracking screen via SignalR — **3.3** (slot designed in **2.3**) |
| **3.4** Fraud & anomaly detection | Fraud dashboard in the support portal: flagged orders/accounts, risk scores, mark-reviewed — **3.1** |
| **3.5** Load testing | Backend-only (k6 targets the API). Frontend analogue: Lighthouse performance budget in **3.4** |
| **3.6** Support Service & ticketing | Ticket queue, detail, messaging, refund workflow, **analytics summary dashboard** — **3.1** |
| **3.7** Production hardening | Frontend equivalent: a11y/perf/PWA/E2E/deploy/README pass — **3.4** |

---

# Part 2 — Detailed Implementation Plan

Each milestone lists: **Goal → Steps → New concepts you learn → Definition of done.**
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
2. Add Tailwind CSS v4 (per official Angular guide: install, add `@import "tailwindcss";` to
   `styles.css`).
3. Add angular-eslint (`ng add angular-eslint`) and Prettier with the Tailwind class-sorting
   plugin (`prettier-plugin-tailwindcss`). Add npm scripts: `lint`, `format`.
4. Enable TypeScript strict options (the CLI default already is strict — verify, don't weaken).
5. Create `environments/` with the three backend base URLs.
6. Verify `ng test` runs the default Vitest suite, `ng build` produces a production build.
7. Commit. Set up a GitHub Actions workflow now, while it's trivial: install → lint → test → build
   on every push touching `Frontend/**`.

**New concepts:** Angular CLI, the dev server, project configuration, how a frontend "build" works
(TypeScript → bundled/minified JS), CI for frontend.

**💡 Hints**
- *Step 1:* run `ng new food-delivery-web --style=css --ssr=false` from inside `Frontend/`. Check
  `node -v` first — the CLI tells you which versions it supports; on Windows use `nvm-windows` if
  you need to switch Node versions.
- *Step 2:* Tailwind v4 has **no `tailwind.config.js`** by default — configuration lives in CSS
  (`@theme { … }` in `styles.css`). If utility classes have no effect: check the `@import
  "tailwindcss";` line is first in `styles.css`, then restart `ng serve` (config changes aren't
  always hot-reloaded).
- *Step 3:* set VS Code `"editor.formatOnSave": true` + Prettier as default formatter now; run
  `npx prettier --write .` once so the first real commit isn't polluted by formatting noise.
- *Step 5:* if the CLI didn't scaffold environments, `ng generate environments` creates them plus
  the `fileReplacements` build config. Type the environment object (`interface Env`) so a typo in
  a URL key is a compile error.
- *Step 7:* in the workflow use `actions/setup-node` with `cache: 'npm'` and run `npm ci` (not
  `npm install` — `ci` respects the lockfile exactly, like `dotnet restore --locked-mode`). Set
  `defaults.run.working-directory: Frontend/food-delivery-web` so every step runs in the right
  folder.
- Install the **Angular DevTools** browser extension today — you'll use its component tree and
  signal inspection constantly.

**Done when:** `ng serve` shows a page styled by a Tailwind class; CI is green on GitHub.

### Milestone 0.B — Design tokens & shared UI kit (2–3 days)

**What & why:** Before building screens, build the LEGO bricks. A small set of reusable components
gives every later screen a consistent look and teaches component API design — the single most
transferable Angular skill.

**Steps**
1. In `styles.css`, define design tokens as CSS variables under Tailwind's `@theme`: brand color
   scale, semantic colors (success/warning/danger), border radius, font. Pick one Google Font.
2. Build in `shared/ui/`, one at a time, each a standalone component using `input()` / `output()`
   signal functions:
   - `app-button` (variants: primary/secondary/danger; sizes; `loading` state that disables + spins)
   - `app-input` (label, error message slot — designed to work with Reactive Forms)
   - `app-card`, `app-badge` (used for order statuses everywhere), `app-spinner`,
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

**Steps**
1. Define top-level routes with lazy loading: `/auth/**`, `/customer/**` (also the default),
   `/restaurant/**`, `/driver/**`, `/admin/**`, plus a `**` not-found page. Each feature gets a
   `<feature>.routes.ts` file loaded via `loadChildren`.
2. Build two layout components in `core/layout/`:
   - **Mobile-first shell** (customer & driver): sticky top bar + **bottom tab navigation** —
     the standard mobile app pattern (Home, Orders, Cart, Profile for customers).
   - **Desktop shell** (restaurant/admin/support): collapsible left sidebar + top bar.
3. Create `AuthService` with a **hard-coded fake user** switchable from a dev-only dropdown in the
   header (Customer / Manager / Driver / Admin). Implement `authGuard` and `roleGuard` against the
   fake user. Real backend auth replaces the internals in Milestone 1.1 — the guards, layouts, and
   role routing won't change.
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
- *Step 3 (fake auth):* keep the fake user in `signal<User | null>` with the same shape the real
  `AuthService` will have — that's what makes the 1.1 swap painless. Show the role-switcher only
  when `isDevMode()` is true.
- *Step 4:* verify lazy loading in devtools Network tab (filter JS): navigating to `/restaurant`
  the first time should fetch a new chunk file. If everything loads upfront, you used a static
  `import` somewhere in a routes file.

**Done when:** switching the fake role and navigating shows the correct shell per role, blocks the
wrong areas, and the network tab shows each area's JS chunk loading only on first visit.

---

## Phase 1 — Core Features (matches backend Phase 1)

> **Goal:** the app works end-to-end against the real backend: register → log in → browse
> restaurants → order → restaurant accepts → status visible. After this phase the project is
> already demo-able.

### Milestone 1.1 — Real authentication (3–5 days)

**What & why:** The frontend's front door, wired to Duende exactly as described in the
[architecture section](#authentication-design-matches-the-existing-backend-exactly).

**Steps**
1. **DTOs & AuthService:** implement `login(email, password)` posting the password-grant form to
   `/connect/token`; parse the JWT payload (base64 decode — no library needed) for user id/email;
   get roles (from claims or `users/profile` — see backend prerequisites). Store tokens; expose
   `currentUser`, `isLoggedIn`, `hasRole()` as signals/computed.
2. **Interceptors:** `authInterceptor` adds the bearer token (skip for the token endpoint);
   `errorInterceptor` maps ProblemDetails → `ApiError`, toasts unexpected errors. On 401: refresh
   once, replay, else logout. (This is the hardest code in the whole app — take it slow, test it
   by temporarily setting an expired token.)
3. **Login page:** typed reactive form, validation messages via `app-input`, loading button,
   "invalid credentials" handling, redirect to role home on success.
4. **Customer registration page:** posts to gateway `users/register` (anonymous), then auto-login.
   Mirror backend validation rules client-side (password rules etc.) — but remember the backend
   remains the source of truth; the form just gives fast feedback.
5. **Invitation activation page** (`/auth/activate`): reads token from the URL query, lets the
   invitee set a password, posts to the set-password endpoint, then routes to login. This
   completes the admin-provisioning story from backend Feature 1.2.
6. **Logout** + session restore on app start (`APP_INITIALIZER`-style: validate stored token,
   fetch profile).
7. Replace the fake auth internals from 0.C; keep the dev role-switcher working via seeded test
   accounts (dev admin from backend config + accounts you create).

**New concepts:** typed reactive forms + validators, HTTP interceptors, JWT anatomy from the
client side, RxJS `switchMap`/`catchError` in the refresh flow, query params, app initialization.

**💡 Hints**
- *Step 1 (token request):* the token endpoint wants
  `application/x-www-form-urlencoded`, **not JSON** — pass an `HttpParams` object as the POST
  *body* and HttpClient sets the content type for you. Sending JSON produces
  `unsupported_grant_type`/`invalid_request` errors that look like backend bugs but aren't.
- *Step 1 (JWT decode):* the payload is **base64url**, not plain base64 — `atob` alone breaks on
  `-`/`_` characters. Write a 5-line helper that replaces them (`-`→`+`, `_`→`/`) before `atob` +
  `JSON.parse`. No JWT library needed.
- *Step 2 (interceptors):* use functional interceptors (`HttpInterceptorFn`) registered via
  `provideHttpClient(withInterceptors([...]))`. Skip attaching the bearer token when the URL is
  the identity host (sending a stale token with a refresh request is a classic loop-starter).
- *Step 2 (refresh loop protection):* mark replayed requests with an `HttpContextToken` so a 401
  on the *retried* request logs out instead of refreshing forever. If several requests 401
  simultaneously, share one in-flight refresh via `shareReplay(1)` — or ship the naive version
  and leave a `// TODO: single-flight refresh` you can discuss honestly.
- *Step 2 (debugging):* a red request with no response and a console message about CORS is the
  **backend prerequisite**, not your code — look for the failed `OPTIONS` preflight in the
  Network tab.
- *Step 3/4 (forms):* build with `NonNullableFormBuilder` (`inject(NonNullableFormBuilder)`) so
  values are typed without `| null` everywhere. Show a field's error only after `touched` — wire
  that logic once into `app-input`, not per page.
- *Step 5:* read the token from the URL with router input binding (`withComponentInputBinding()`
  in `provideRouter`, then `token = input<string>()` in the page).
- *Step 6:* register session restore with `provideAppInitializer(...)` in `app.config.ts` so the
  router doesn't start before you know whether the user is logged in (otherwise guards redirect
  to login on every F5).
- *Testing refresh:* set the access token lifetime to ~60 s in the Duende client config and watch
  the Network tab do a token call mid-session, invisible to the UI.

**Done when:** all auth flows work against the running backend; refresh is observable (set access
token lifetime low and watch the network tab); a full page reload keeps you logged in.

### Milestone 1.2 — Customer: browse restaurants & menus (3–4 days)

**Steps**
1. `RestaurantsApi` client + DTOs for restaurant search and menu endpoints (from Swagger).
2. **Restaurant list page** (customer home): mobile-first card grid (1 column on phone, 2–3 on
   larger screens), each card = logo, name, cuisine, rating placeholder. Server-side pagination
   ("Load more" button — simpler than infinite scroll, fine for portfolio).
3. **Search & filters:** search box with `debounceTime(300)` + `switchMap` (the classic RxJS
   interview example — implement it once, understand it forever), cuisine filter chips, and a
   minimum-rating filter (once ratings exist in 2.4). Keep search state in the URL query params
   so the back button and refresh work.
4. **"Near me" proximity search:** ask for browser geolocation (with a graceful "enter your area
   manually" fallback when denied) and pass the coordinates to the backend's proximity search;
   show distance on each card. This is your first taste of the Geolocation API before the driver
   portal leans on it hard in Phase 2.
5. **Restaurant detail / menu page:** header with restaurant info + opening hours, menu grouped by
   category with a sticky category tab bar, sold-out items visibly disabled.
6. Loading skeletons and `app-empty-state` for no-results; error state with retry.

**New concepts:** container/presentational split in practice, debounced search, URL-as-state,
browser geolocation basics, skeleton loading UX, rendering nested backend data.

**💡 Hints**
- *Step 2 (grid):* `grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4` is the whole layout.
  For "Load more", append to a `signal<Restaurant[]>` and track the next page number — resist
  infinite scroll (IntersectionObserver) until everything else works.
- *Step 3 (search):* the canonical pipe is `search.valueChanges.pipe(debounceTime(300),
  distinctUntilChanged(), switchMap(term => api.search(term)))` — `switchMap` *cancels* the
  in-flight request when a new keystroke arrives; that cancellation is the whole point and the
  interview answer.
- *Step 3 (URL state):* write filters with `router.navigate([], { queryParams: { q, cuisine },
  queryParamsHandling: 'merge' })` and read them back via route input binding. If state lives
  only in component fields, refresh/back will lose it — the URL *is* the store here.
- *Step 4 (geolocation):* `navigator.geolocation.getCurrentPosition` is callback-based — wrap it
  in a `Promise` once, in a small `GeolocationService`. It only works on `localhost` or HTTPS,
  and the user can deny — treat "denied" as a normal state (show the manual fallback), not an
  error.
- *Step 5 (sticky tabs):* `sticky top-0 z-10` on the tab bar + `scrollIntoView({ behavior:
  'smooth' })` on tab click gets you 90% of the effect; highlighting the active section while
  scrolling needs `IntersectionObserver` — skip it if it fights back.
- *Step 6 (skeletons):* gray boxes matching the card's real dimensions + Tailwind's
  `animate-pulse`. Show them on *initial* load only; "Load more" gets a spinner on the button
  instead.

**Done when:** you can find a seeded restaurant on a phone-sized screen in under three taps and it
feels like a real food app.

### Milestone 1.3 — Customer: cart & checkout (3–4 days)

**Steps**
1. **`CartService`** — the state-management showcase: `signal<CartItem[]>`, `computed` subtotal /
   count, add/remove/change-quantity methods, persisted to `localStorage` (survives refresh),
   one-restaurant-per-cart rule (adding from another restaurant prompts to clear — standard UX).
2. **Menu integration:** add-to-cart buttons with quantity steppers; cart icon with `computed`
   item-count badge in the shell.
3. **Cart page/drawer:** line items, edit quantities, totals.
4. **Checkout page:** delivery address form (typed reactive form), payment method fixed to "Cash
   on delivery", order summary. On submit: generate an **idempotency key** (`crypto.randomUUID()`
   created *when checkout opens*, sent as a header) — this pairs with the backend's idempotent
   order placement and is a great cross-stack story to tell.
5. **Order placement** via `OrdersApi` → success screen → clear cart → link to order detail.
   Handle the failure case where menu prices changed since the cart was built (backend validates —
   show which items changed).

**New concepts:** shared client state with signals (the heart of frontend thinking — state that
exists only in the browser), `effect()` for localStorage persistence, optimistic vs. confirmed UI,
idempotency from the client side.

**💡 Hints**
- *Step 1 (persistence):* one `effect(() => localStorage.setItem('cart',
  JSON.stringify(this.items())))` in the service constructor persists every change automatically.
  Hydrate in the constructor with a `try/catch` around `JSON.parse` — a corrupt value must clear
  the cart, not crash the app.
- *Step 1 (money):* JavaScript floats will happily tell you `0.1 + 0.2 = 0.30000000000000004`.
  Keep prices as the numbers the backend sends, do arithmetic in a `computed`, and round **only
  at display time** in your money pipe (`Intl.NumberFormat`). Never accumulate rounded values.
- *Step 1 (one-restaurant rule):* store `restaurantId` on the cart itself; on add-from-elsewhere,
  open the confirm modal and clear on confirm — copy the UX of any big delivery app.
- *Step 4 (idempotency key):* create it with `crypto.randomUUID()` **when the checkout page
  opens** (a component field), not inside the submit handler — a double-click must reuse the
  same key, that's the entire mechanism.
- *Step 4 (double-submit):* also set a `submitting` signal that disables the button; the
  idempotency key is the backend guarantee, the disabled button is the UX guarantee. You want
  both, and you can name that distinction in interviews.
- *Step 5 (price-changed failure):* the backend rejects stale prices — match the returned
  ProblemDetails against cart items and highlight the changed lines instead of a generic toast.
  This is the first place your error-handling design pays off.

**Done when:** the classic demo works end-to-end: browse → add items → checkout → order row exists
in the backend DB; double-clicking "Place order" creates exactly one order.

### Milestone 1.4 — Customer: my orders & order detail (2–3 days)

**Steps**
1. **Orders list page:** active orders on top, history below; status shown with `app-badge` and a
   shared `orderStatus` pipe/mapping (color + label per status — reused by every role area).
2. **Order detail page:** items, totals, address, and a **status timeline** component (Pending →
   Accepted → Preparing → Ready → Out for delivery → Delivered) — visual, mobile-friendly, built
   once and reused by restaurant and driver views.
3. **Cancel order** where the state machine allows it, with `app-modal` confirmation; surface the
   backend's rule violations (Result failures) as friendly messages.
4. Polling refresh (every ~15 s on the detail page) as a stopgap — explicitly replaced by SignalR
   in Phase 2 (a nice before/after story).

**💡 Hints**
- *Steps 1–2 (status metadata):* define one
  `ORDER_STATUS_META: Record<OrderStatus, { label: string; color: string; step: number }>`
  constant and drive the badge, the timeline, and later the manager/driver views from it. When a
  status rendering looks wrong anywhere, there's exactly one place to fix.
- *Step 2 (timeline):* it's a list of steps compared against the current status's `step` index —
  completed/current/upcoming get different Tailwind classes. Handle the branch: Rejected and
  Cancelled aren't steps on the line, render them as a terminal banner instead.
- *Step 3 (cancel):* the backend's state machine is the source of truth — on failure show the
  ProblemDetails `detail` message ("order already accepted"), don't try to replicate every rule
  client-side. Only hide the button in states where cancelling is *obviously* impossible.
- *Step 4 (polling):* `interval(15_000).pipe(startWith(0), switchMap(() => api.getOrder(id)))`
  with `takeUntilDestroyed()` so navigation away stops it. `startWith(0)` makes the first load
  immediate — forgetting it means a 15-second blank page.

**Done when:** placing an order and having the backend move it through states (via Swagger for
now) is fully visible in the customer UI.

### Milestone 1.5 — Restaurant Manager portal (4–6 days)

**What & why:** The other side of the marketplace, in the desktop shell. First heavy CRUD work —
where reactive forms really pay off.

**Steps**
1. **Incoming orders dashboard:** new (Pending) orders as prominent cards with items + accept /
   reject buttons (reject requires a reason — modal). Columns/sections per active status; buttons
   to advance status (Start preparing → Ready for pickup) following the state machine. Poll every
   ~10 s until Phase 2 real-time replaces it.
2. **Menu management:** category list with create/rename/delete/reorder; menu item CRUD in a
   drawer/modal form (name, description, price, photo URL, availability toggle). An
   **availability toggle** flips items to "sold out" instantly from the list — the single most-used
   manager action, so it gets first-class UX.
3. **Restaurant profile page:** edit description, opening hours (per-day open/close — a nice
   `FormArray` exercise), cuisine type.
4. Guard everything with `roleGuard('RestaurantManager')`; the backend enforces ownership — the
   frontend just handles 403s gracefully.

**New concepts:** CRUD-heavy forms, `FormArray`, edit-in-place UX, optimistic updates with
rollback on error (do it for the availability toggle only), handling authorization failures.

**💡 Hints**
- *Step 1 (dashboard):* reuse the polling recipe from 1.4 (10 s interval). Highlight orders that
  arrived since the last poll (compare ids, flash a ring) — cheap code, big "live" feel until
  real SignalR lands in 2.1.
- *Step 2 (item form):* one form component used for both create and edit — pass an optional
  `item` input and `patchValue` when present. Price input: `<input type="number" step="0.01">`
  plus a validator for `> 0`; remember the value arrives as a number *or* string depending on
  browser — normalize in one place.
- *Step 2 (availability toggle):* the optimistic recipe: flip the signal immediately → fire the
  API call → on error flip back and toast. Do it for this one control only; everywhere else,
  boring "wait for the server" updates are the right default.
- *Step 2 (reordering):* up/down arrow buttons that swap positions are completely fine. Drag &
  drop (`@angular/cdk/drag-drop`) is a fun stretch, not a requirement.
- *Step 2 (photos):* a URL input + live `<img>` preview with an `(error)` fallback image. Real
  file upload means backend blob storage — scope creep, skip it.
- *Step 3 (opening hours):* `FormArray` of 7 groups `{ day, open, close, closed }` with `<input
  type="time">`. In the template, iterate `hours.controls` with `@for (…; track $index)` — the
  `formArrayName`/index wiring is fiddly the first time; get one row working before styling.
- *Step 4 (403s):* the error interceptor should turn 403 into a "You don't have access to this"
  toast + redirect to the area home — build it once here, every later portal inherits it.

**Done when:** a manager can run their restaurant for a day without touching Swagger: see a new
order, accept it, progress it, and sell out an item.

### Milestone 1.6 — Administrator portal (2–3 days)

**Steps**
1. **Restaurant onboarding wizard** (2 steps): restaurant data (name, tax id, address, cuisine,
   commission %) → manager account (email, name) → submit to the backend onboarding endpoint →
   success screen explaining the invitation email was sent.
2. **Staff/partner account provisioning:** simple form (email, name, role: Driver / Support Agent /
   Administrator) → invite endpoint; list of provisioned accounts with invitation status if the
   backend exposes it.
3. Now close the loop you built in 1.1: provision an account → grab the activation link (from
   the dev email sink, e.g. Mailpit) → activate in the SPA → log in with the new role. **This
   end-to-end flow across Identity, Users, email, and the SPA is one of the strongest demos in
   the whole project.**

**💡 Hints**
- *Step 1 (wizard):* don't route between steps — one parent component with a `step = signal(1)`
  and one child form per step. Advance only when the current step's form group is valid
  (`markAllAsTouched()` on a failed "Next" so errors show). Keep both groups alive so "Back"
  preserves input.
- *Step 1 (commission input):* percentage field — validate `0–100` and display with a `%` suffix;
  decide (and document) whether the backend wants `12.5` or `0.125` *before* wiring it.
- *Step 2:* after a successful invite, show the invited email + a "Copy invite link" button
  (`navigator.clipboard.writeText(...)`) if the API returns the activation link — invaluable for
  your own testing loop.
- *Step 3 (finding the email):* the dev mail sink (e.g. Mailpit in `docker-compose`) has a web UI
  — check the compose file for its port. The activation link in the email must point at your SPA
  (`http://localhost:4200/auth/activate?...`) — if it doesn't, that's the backend-prerequisite
  item, fix the email template, not the frontend.

**Done when:** you can onboard a fresh restaurant + manager and they can log in and see their
(empty) restaurant — without touching the database or Swagger.

> ✅ **Phase 1 checkpoint:** tag a release, record a 2-minute demo GIF for the README, and take a
> breath — you now have a full-stack marketplace. Everything after this is depth.

---

## Phase 2 — Real-Time & Driver Experience (matches backend Phase 2)

> **Goal:** the app comes alive — statuses update by themselves, drivers get a real mobile
> workflow, and customers watch their food travel on a map.

### Milestone 2.1 — SignalR foundation (2–3 days)

**Steps**
1. `RealtimeService` in `core/realtime/`: wraps one `HubConnection` to the realtime service
   (:5600), authenticated via `accessTokenFactory` (reuses `AuthService` tokens), with automatic
   reconnect and a connection-state signal (show a subtle "reconnecting…" indicator in the shell).
2. Start/stop the connection based on login state (`effect()` watching `isLoggedIn`).
3. Bridge hub events to the app as RxJS `Subject`s or signals per event type (`orderStatusChanged$`,
   `driverLocation$`), matching the hub contracts in `REALTIME_PHASE2_PLAN.md`.
4. Replace the polling from 1.4/1.5: customer order detail and restaurant dashboard update
   instantly. Delete the polling code with a satisfied commit message.
5. **In-app notifications:** a bell icon in the shell with an unread badge and a dropdown/sheet
   listing recent events ("Your order was accepted", "Driver assigned: Marko"), fed by the same
   SignalR events and kept in a signal-based `NotificationsService`. This is the frontend face of
   the backend's Notification story (emails stay backend-side; browser push is a Phase 3 stretch).

**New concepts:** WebSockets from the client, connection lifecycle management, push-based UI
updates, translating server events into signal updates.

**💡 Hints**
- *Step 1 (connection):* the incantation is `new HubConnectionBuilder().withUrl(url, {
  accessTokenFactory: () => this.auth.accessToken() }).withAutomaticReconnect().build()`.
  `accessTokenFactory` may return a `Promise<string>` — refresh there if the token is about to
  expire, otherwise reconnects after a long idle will 401.
- *Step 1 (auth transport):* browsers can't set headers on WebSocket connects, so SignalR passes
  the token as a query string — the realtime backend must read it from there (the realtime plan
  covers this; if the hub rejects you with 401, look server-side first).
- *Step 2:* register **all** `.on('EventName', handler)` listeners *before* calling `.start()` —
  events that arrive before a handler is registered are dropped silently, which looks exactly
  like "SignalR randomly doesn't work".
- *Step 3:* inside handlers just write to signals (`this.orderStatus.set(...)`) — the UI updates
  automatically; no change-detection tricks needed. Log every received event to the console in
  dev; you will thank yourself.
- *Step 4 & testing:* use one normal window + one incognito window for two different logins.
  Simulate flaky networks with devtools → Network → "Offline" and watch `withAutomaticReconnect`
  do its thing; the connection-state signal should visibly move disconnected → reconnecting →
  connected.

**Done when:** two browser windows (customer + manager) side by side: manager clicks Accept, the
customer's timeline advances with no refresh. Toast on status change if the customer is elsewhere
in the app.

### Milestone 2.2 — Driver portal (5–7 days)

**What & why:** The most mobile-critical part of the entire product — a driver uses this while
standing next to a scooter. Big touch targets, one primary action per screen, works one-handed.

**Steps**
1. **Driver home:** giant online/offline toggle (calls availability endpoints), current status,
   today's summary. While online, send geolocation updates: `navigator.geolocation.watchPosition`
   → throttle to every few seconds → location update endpoint. Handle permission-denied with a
   clear explanation screen.
2. **Assignment offer screen:** when the backend assigns a delivery (event via SignalR), show a
   full-screen offer — restaurant, distance, destination — with Accept / Reject and the
   backend-driven expiry countdown (Quartz expiry job on the backend). This is the most
   "app-like" screen in the project.
3. **Active delivery flow:** one screen, one state machine (mirroring backend Delivery states):
   navigate-to-restaurant → **Mark picked up** → navigate-to-customer → **Mark delivered**.
   Leaflet map with restaurant/customer pins and the driver's own live position; a link out to
   Google Maps/Waze for actual navigation (what real driver apps do).
4. **Delivery history list** with earnings-free summary (no payments in this project).
5. Test outdoors once with a phone on the local network (serve with `--host 0.0.0.0`; geolocation
   needs HTTPS or localhost — use a dev tunnel or accept emulated locations in dev).

**New concepts:** browser Geolocation API, permissions UX, Leaflet maps (markers, panning),
throttling high-frequency updates, designing for one-handed phone use.

**💡 Hints**
- *Step 1 (geolocation):* `watchPosition` returns a watch id — store it and `clearWatch(id)` when
  going offline, or the phone keeps the GPS hot forever. Throttle sends with `throttleTime(3000)`
  (or timestamp comparison) — GPS can fire several times a second.
- *Step 1 (simulating movement):* Chrome devtools → ⋮ → More tools → **Sensors** lets you set a
  fake location; for continuous movement, a dev-only "simulate route" button that emits
  interpolated coordinates on a timer beats fighting devtools.
- *Step 3 (Leaflet setup):* install `leaflet` + `@types/leaflet`; add `leaflet/dist/leaflet.css`
  to the `styles` array in `angular.json` (missing CSS = gray tiles/broken layout). Create the
  map in `ngAfterViewInit`, never in the constructor, and call `map.invalidateSize()` if the map
  initializes inside a hidden/animating container — the "map renders as a gray square" bug is
  always one of these two.
- *Step 3 (marker icons):* Leaflet's default marker images 404 under bundlers — the well-known
  fix is overriding `L.Icon.Default` with imported image URLs, or sidestep it entirely with
  `L.divIcon` + a Tailwind-styled div (a colored dot for the driver looks better anyway).
- *Step 2 (countdown):* compute time-left from the **server-sent expiry timestamp** on every
  tick, don't count down from "when I received it" — a tab in the background throttles timers
  and your local countdown drifts from the backend's Quartz expiry.
- *Step 5 (phone testing):* `ng serve --host 0.0.0.0` and open `http://<laptop-ip>:4200` on the
  phone — but geolocation needs a secure context, so plain HTTP fails: use VS Code's port
  forwarding (gives an HTTPS URL) or a dev tunnel. Emulated locations on desktop are fine for
  most of this milestone.
- *UX:* primary action buttons full-width at the bottom of the screen (thumb zone), min height
  ~56 px, one primary action per screen. Add `navigator.vibrate(200)` on new assignment — tiny
  API, delightful demo.

**Done when:** with backend + simulated movement, a driver can go online, get an offer, accept,
pick up, and deliver — driving the customer's order to Delivered, all touch-only.

### Milestone 2.3 — Customer live tracking map (2–3 days)

**Steps**
1. On the customer order-detail page for Out-for-delivery orders: Leaflet map with restaurant,
   home, and the driver marker moving on `driverLocation$` events (subscribe to the order's
   tracking group on the hub).
2. Smooth the marker movement (simple interpolation between points — nice-to-have).
3. Show ETA text when the backend provides it (Phase 3 feature — render "-" until then; design the
   slot now).

**💡 Hints**
- *Step 1 (reuse):* extract a shared `app-map` component from the driver work in 2.2 (inputs:
  markers, center; output: nothing) — two hand-rolled Leaflet setups will drift apart.
- *Step 1 (groups):* join the order's tracking group on init (`hub.invoke('JoinOrderTracking',
  orderId)` or whatever the hub contract names it) and **leave it on destroy** via
  `inject(DestroyRef).onDestroy(...)` — forgetting to leave means you keep receiving another
  order's coordinates on the next tracking page.
- *Step 1 (bounds):* call `map.fitBounds([...])` **once** with restaurant + home + driver, then
  stop touching the viewport — re-fitting on every location update makes the map lurch and users
  seasick. Offer a "re-center" button instead.
- *Step 2:* simplest smoothing that looks good: `requestAnimationFrame`-lerp the marker from its
  previous position to the new one over ~1 s. Plain `setLatLng` jumps are acceptable; smooth
  movement is the demo upgrade.

**Done when:** the full theater demo works: phone (driver, moving mock locations) + laptop
(customer) — the marker moves live. *This is the money shot for the portfolio README GIF.*

### Milestone 2.4 — Reviews & ratings (2–3 days)

**Steps**
1. Post-delivery review prompt on the order detail (1–5 stars restaurant + separate delivery
   rating + text) — a reusable `app-star-rating` component (keyboard accessible: arrow keys).
2. Enforce one-review-per-order in the UI (hide the form if reviewed); backend enforces it for real.
3. Ratings surface: average stars + count on restaurant cards and detail header; reviews list with
   pagination on the restaurant page; manager sees their reviews read-only in the portal.

**💡 Hints**
- *Step 1 (stars):* five buttons in a row; a `hovered` signal drives the preview fill on
  `mouseenter`, click commits to the form value. Accessibility: wrap as `role="radiogroup"`, each
  star `role="radio"` with `aria-label="3 stars"`, arrow keys move the value — a small,
  well-known pattern worth doing right (it's a favorite code-review topic).
- *Step 1 (partial stars for averages):* render the star row twice, filled over unfilled, and
  clip the filled row with `overflow-hidden` at `width: {avg/5*100}%` — no SVG math needed.
- *Step 2:* drive "can review?" from the backend (order status Delivered + no existing review) —
  fetch it with the order instead of guessing client-side; after submit, flip local state so the
  form hides immediately.
- *Step 3:* the average on cards comes from the search response (backend caches it in Redis) —
  don't compute averages client-side from the reviews list; the two would disagree and confuse
  you into "fixing" the wrong layer.

**Done when:** deliver → review → the restaurant's average visibly updates in search results.

---

## Phase 3 — Support, AI & Production Polish (matches backend Phase 3)

> **Goal:** the differentiators — support tooling, AI features surfaced in the UI — and the final
> quality pass that makes reviewers take the project seriously.

### Milestone 3.1 — Support Agent portal (3–5 days)

**Steps**
1. **Ticket queue:** filterable table (status, date), claim/assign to me, status workflow
   (Open → In Progress → Resolved / Escalated).
2. **Ticket detail:** customer + full order context, the AI chatbot transcript that preceded
   escalation, internal agent↔customer messaging thread, refund-request action (records the
   request; no payments), and audit-relevant actions always requiring a reason.
3. **Fraud dashboard** (backend Feature 3.4): flagged orders/accounts with risk scores and the
   signals that triggered them; mark-reviewed workflow; simple trend chart of flags per day.
4. **Review moderation** (backend Feature 2.6): list of reported/abusive reviews with a hide/
   restore action, always with a reason (feeds the backend's audit logging).
5. **Support analytics summary** (backend Feature 3.6): small dashboard — average resolution time,
   tickets per day, most common issue types. Render with a lightweight chart approach (a few bars
   built with plain divs + Tailwind is fine; a chart library is optional, not required).

**New concepts:** data-dense desktop tables (sorting/filtering/pagination as reusable patterns),
multi-pane layouts, timeline/chat rendering, simple data visualization.

**💡 Hints**
- *Step 1 (tables):* resist building a generic `<app-table>` component — it's a classic rabbit
  hole. A plain `<table>` per page with shared Tailwind classes and small reusable pieces
  (pagination bar, sort-header component) gets you everything with a tenth of the complexity.
  Wrap tables in `overflow-x-auto` so they survive narrow screens.
- *Step 1 (filters):* same URL-as-state recipe as milestone 1.2 — filters in query params, so an
  agent can bookmark "open tickets, oldest first".
- *Step 2 (detail layout):* two-pane on desktop (`grid grid-cols-[2fr_1fr]`), stacked on smaller
  screens; the conversation thread reuses the chat-bubble rendering you'll also want in 3.2 —
  build the bubble component once in `shared/ui`.
- *Steps 3/5 (charts):* a bar chart is `flex items-end` + divs with `height: (value/max)*100%` +
  tooltips via `title` — genuinely enough here. If you want a library, Chart.js is the
  boring-good choice; by this milestone you can afford it.
- *Step 4:* "hide" is a status change with a mandatory reason (modal), not a delete — mirror the
  backend's audit-logging mindset in the UI copy ("Hidden by support, reason: …").

### Milestone 3.2 — AI chatbot UI (3–4 days)

**Steps**
1. Floating chat button + slide-up chat panel in the customer area (full-screen sheet on mobile).
2. Message list (user/bot bubbles, typing indicator), input with send-on-Enter, conversation kept
   per session (matches the backend chat-history store).
3. If the backend streams responses, render tokens as they arrive; otherwise show the typing
   indicator until the reply lands. Render the escalated-to-human handoff state distinctly.
4. Quick-action chips ("Where is my order?", "Cancel my order") that pre-fill the input — good UX
   and great demo ergonomics.

**New concepts:** chat UX patterns, auto-scrolling, optimistic message rendering, (optionally)
consuming streamed HTTP responses.

**💡 Hints**
- *Step 2 (auto-scroll):* after appending a message set `container.scrollTop =
  container.scrollHeight` — but **only if the user was already near the bottom** (check before
  appending); yanking the view while someone reads an old message is the most common chat-UX bug.
- *Step 2 (optimistic send):* push the user's bubble into the messages signal immediately, then
  show a typing indicator (three bouncing dots = three divs with `animate-bounce` and staggered
  `animation-delay`) until the bot reply arrives; on send failure mark the bubble with a retry
  affordance.
- *Step 2 (input):* `<textarea rows="1">` that grows: set `height:auto` then
  `height:scrollHeight` on input, cap with `max-h-32`. Enter sends, Shift+Enter adds a newline.
- *Step 3 (streaming):* `HttpClient` buffers responses — token-by-token streaming needs `fetch` +
  `ReadableStream` reader appending to a signal. It's ~20 lines but a separate concept; timebox
  it, the typing indicator alone demos fine.
- *Step 3 (handoff):* render the escalation as a system message in the thread ("Connecting you to
  an agent…") + a visually distinct agent bubble style — the same transcript then appears in the
  support portal (3.1), which is a great cross-portal demo moment.
- *Mobile:* the slide-up panel is `fixed inset-0` + `h-dvh` with the input pinned above the
  keyboard; test on a real phone — software keyboards eat fixed-bottom inputs (`interactive-widget`
  viewport meta / `dvh` units are the knobs to reach for).

### Milestone 3.3 — AI surfaces: recommendations & live ETA (2–3 days)

**Steps**
1. **Home personalization:** "Recommended for you" carousel (with the AI's reasoning as a subtle
   subtitle — differentiating and honest) + "Trending near you" section, from backend Feature 3.2
   endpoints.
2. **Live ETA:** fill the ETA slot from 2.3 — show the dynamic estimate on checkout, order detail,
   and tracking; update via SignalR as the backend refines it (Feature 3.3).

**💡 Hints**
- *Step 1 (carousel):* a horizontal scroll container with `flex overflow-x-auto snap-x
  snap-mandatory` and `snap-start` on cards is a complete, touch-native carousel — no library,
  no JS.
- *Step 1 (slow AI endpoint):* recommendation calls hit an LLM — treat them as slow by design:
  skeleton row while loading, and a fallback to "popular restaurants" if the call errors or takes
  more than a few seconds (`timeout()` from RxJS). The home page must never be blocked by the AI
  feature.
- *Step 1 (reasoning subtitle):* truncate to one line with `line-clamp-2` — model-generated text
  varies wildly in length and will wreck card layouts otherwise.
- *Step 2 (ETA display):* show a *range* ("19:25–19:35"), computed once from the backend estimate
  — ranges absorb model error and look more honest than fake precision. When a SignalR update
  shifts the ETA, animate the change subtly (a brief highlight) so users notice without alarm.

### Milestone 3.4 — Production polish (4–6 days, spread out)

The checklist that separates "student project" from "hire this person":

1. **PWA:** add `@angular/pwa` — installable on a phone home screen with an icon and offline app
   shell. For a food-delivery app this is *the* fitting finishing touch, and it's cheap.
2. **Accessibility pass:** keyboard-navigate every flow; labels on all inputs; focus trap in
   modals; `alt` texts; check color contrast of your tokens; run Lighthouse a11y audit ≥ 95.
3. **Performance pass:** run Lighthouse on the customer area (mobile preset); ensure lazy chunks
   are sensible (`ng build` bundle stats); add `@defer` for below-the-fold heavy bits (map,
   reviews); `NgOptimizedImage` for logos/photos.
4. **Error & edge polish:** offline banner (`navigator.onLine`), 404 page, empty states
   everywhere, form double-submit protection audit.
5. **E2E suite:** 3–5 Playwright tests — login, browse+order happy path, manager accept, guard
   redirect. Wire into CI.
6. **Deploy:** Azure Static Web Apps (free tier) via GitHub Actions; point it at the deployed
   backend from backend Feature 1.7/2.5. Custom README section: architecture diagram including
   the frontend, screenshots/GIFs (tracking map!), link to the live demo.
7. **(Optional) Browser telemetry:** add the Application Insights JavaScript SDK so page views,
   AJAX timings, and frontend errors land in the same Application Insights instance as the
   backend traces (backend Feature 2.4) — end-to-end correlation from a button click to a SQL
   query is a spectacular interview demo.
8. **(Optional) Web push notifications:** the PWA from step 1 enables browser push — subscribe
   via the service worker and let the backend Notifications service send order updates even when
   the tab is closed. Completes the Phase 2 promise from backend Feature 1.6; skip if time is
   short, the in-app bell (2.1) already covers the story.
9. **(Optional stretch)** refactor `CartService` to `@ngrx/signals` SignalStore and write one
   paragraph in the README comparing the two — interview gold.

**💡 Hints**
- *Step 1 (PWA):* the service worker is **disabled in `ng serve`** — test with a production
  build: `ng build` then serve `dist/` with `npx http-server`. While developing, keep devtools →
  Application → Service Workers → "Update on reload" checked, or you'll spend an afternoon
  debugging a stale cached bundle (everyone does this once).
- *Step 2 (a11y):* fastest wins first: every `app-input` already has a label (0.B pays off),
  `alt` on images, visible focus rings (don't remove Tailwind's defaults), Escape closes modals
  (free with `<dialog>`). Then run Lighthouse and fix what it lists — it names exact elements.
- *Step 3 (bundles):* `ng build` prints per-chunk sizes; investigate with
  `npx esbuild-visualizer` or source-map-explorer if a chunk balloons. Likely suspect: Leaflet
  imported eagerly — confirm it only lives in lazy chunks, and wrap map/reviews sections in
  `@defer (on viewport)`.
- *Step 5 (Playwright):* use the `webServer` option in `playwright.config.ts` so tests auto-start
  `ng serve`; select elements by role/label (`getByRole('button', { name: 'Place order' })`) —
  resort to `data-testid` only when that fails. Seed a dedicated test user; never depend on state
  a previous test created.
- *Step 6 (SPA deploy):* deep links 404 on static hosts until you add the SPA fallback — for
  Azure Static Web Apps that's `staticwebapp.config.json` with `navigationFallback` →
  `/index.html`. Also swap `environment.ts` URLs at build time via the production file
  replacement, not by hand.
- *Step 7 (browser telemetry):* correlation only works if the App Insights JS SDK's
  `enableCorsCorrelation` is on **and** the backend's CORS policy allows the correlation headers
  (`Request-Id`, `traceparent`) — otherwise you get frontend telemetry that never joins the
  backend traces.

---

## Conventions & Working Rules

Small set — consistency beats cleverness:

- **Components:** standalone; `changeDetection: ChangeDetectionStrategy.OnPush` everywhere
  (signals make this free); templates use `@if`/`@for`; inputs/outputs via `input()`/`output()`
  functions; inject dependencies with `inject()`.
- **Naming:** files `kebab-case` (CLI default); one component per file; page components end in
  `*Page` (`OrderDetailPage`), presentational ones don't.
- **DTOs:** TypeScript `interface`s in `core/api/models/`, named exactly like the backend
  responses. Dates arrive as ISO strings — keep them as strings in DTOs, convert at the edge
  (pipes) — a classic beginner trap.
- **No `any`.** If you're tempted, the DTO is missing.
- **Tailwind:** mobile-first always (base classes = phone, `md:` adds desktop); extract repeated
  class clusters into a shared component rather than `@apply`.
- **Git:** same discipline as the backend — feature branches, conventional-ish commits
  (`feat(customer): cart drawer`), PRs even solo (CI must pass).
- **One `TODO(frontend-plan)` grep before each phase checkpoint** — no silent leftovers.

## Testing Strategy

Pragmatic pyramid — enough to prove the skill, not test theater:

| Layer | Tool | What to cover |
|---|---|---|
| Unit | Vitest | The logic-bearing things: `CartService` (totals, one-restaurant rule), `AuthService` (refresh decision logic), pipes, order-status mapping. Aim for *meaningful* tests, not coverage %. |
| Component | Vitest + Angular testing utilities | A handful: login form validation display, star-rating interaction, status timeline rendering per status. |
| E2E | Playwright | 3–5 happy paths (Milestone 3.4). |

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
5. Keep a `LEARNING_NOTES.md` — one line per "aha". It becomes your interview prep doc for the
   frontend side, feeding the same interview-questions docs you keep for the backend.

---

*Execute incrementally. Phase 0 + Phase 1 alone produce a demo-able full-stack product; each later
phase adds visible wow (live maps, AI chat) on top of a solid foundation. Keep the backend the
star of the show — the frontend's job is to make it undeniable.*
