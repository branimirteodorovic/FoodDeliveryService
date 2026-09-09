# API documentation

> Delivered by **Feature 3.7 — Final Production Hardening** (`HARDENING_PHASE3_PLAN.md`),
> **Milestone G**. Seven services, seven OpenAPI documents, two UIs over each, all reachable through
> the one public entry point. The authorization posture it ships with is in
> [`security.md`](security.md) §5.1 (the CSP carve-out) and §1 (the permission model the operations
> name).

## 1. Where the documentation is

Every service serves its own document and both UIs under `/docs/{slug}/`, and the Gateway proxies
`docs/{slug}/**` through with an identity path transform — so the URL is the same whether you reach
the service directly or through the edge. On a `docker-compose up` stack the module hosts publish
ephemeral ports, so the Gateway is in practice the only stable way in:

| Service | Landing page | OpenAPI document |
|---|---|---|
| Orders | `http://localhost:3000/docs/orders` | `…/docs/orders/openapi/v1.json` |
| Restaurants | `http://localhost:3000/docs/restaurants` | `…/docs/restaurants/openapi/v1.json` |
| Users | `http://localhost:3000/docs/users` | `…/docs/users/openapi/v1.json` |
| Delivery | `http://localhost:3000/docs/delivery` | `…/docs/delivery/openapi/v1.json` |
| Support | `http://localhost:3000/docs/support` | `…/docs/support/openapi/v1.json` |
| RealTime | `http://localhost:3000/docs/realtime` | `…/docs/realtime/openapi/v1.json` |
| Notifications | `http://localhost:3000/docs/notifications` | `…/docs/notifications/openapi/v1.json` |
| Payments | `http://localhost:3000/docs/payments` | `…/docs/payments/openapi/v1.json` |

Each landing page redirects to **Scalar** at `/docs/{slug}/scalar`. **Swagger UI** renders the same
document at `/docs/{slug}/swagger`. Both are kept: Swagger UI is the one every .NET reviewer already
knows, Scalar is the one worth showing someone.

Three of those documents are empty, and all three deliberately:

- **Notifications** is a pure event consumer — it reacts to integration events and sends email, and
  has never exposed an HTTP endpoint.
- **RealTime**'s only endpoint is the `hubs/tracking` SignalR hub, and `MapHub` contributes no
  `ApiDescription`, so a hub cannot appear in a generated OpenAPI document however it is annotated.
  What a client needs to know about the handshake is in that service's description instead.
- **Payments** is a skeleton. Feature 3.8 Milestone B shipped the service, its host, its database and
  every registration it needs, with no business logic behind them; its endpoints arrive in the
  milestones after it. It is documented from day one on the same argument that publishes
  Notifications' empty document — a service with an empty API surface and a service whose
  documentation was forgotten look identical otherwise.

`OpenApiDocumentTests` asserts all three, so "no operations" cannot quietly become a symptom instead
of a decision.

### Why not one aggregated document at the Gateway

There isn't one to aggregate. These are separately deployed services with a schema each; a merged
document would imply a single API surface behind a single process, which is the exact thing the old
shared title ("built using the modular monolith architecture", on all seven) got wrong. The
Gateway routes to each document instead of pretending to own them.

## 2. Reachability and authorization

The documentation used to be mapped inside `if (app.Environment.IsDevelopment())`, which meant the
documented surface was invisible from every environment anyone other than the author would look at.
It is now mapped everywhere, and gated instead:

| Environment | Documentation surface |
|---|---|
| `Development` | Anonymous. The host logs a warning at startup saying so. |
| Anything else | Requires a valid bearer token — any authenticated caller. |

The bar outside Development is **authentication, not a permission**. There is no `docs:read` in the
seeded permission set and inventing one would put a Users seed migration in the way of every
documentation change; "hold a token this platform issued" is what keeps a complete, machine-readable
inventory of every route, parameter and permission off the open internet, which is the actual
threat. Reading about `POST orders/{id}/accept` has never been the same thing as being allowed to
call it — the endpoints stay individually authorized either way.

**The Gateway's docs routes are `anonymous`, and that is not a contradiction.** YARP's routing table
is static configuration with no idea what environment the downstream service is running in, so a
`default` policy there would also 401 a developer browsing a local compose stack — the one place the
UI is supposed to be readable. The enforcement point is the service, which knows its own
environment. The gateway route says *do not decide here*.

A deployment that wants the surface gone rather than gated sets `ApiDocumentation:Enabled` to
`false`; the endpoints are then never mapped at all.

## 3. Getting a token to try a call

Both UIs have an "Authorize" button that takes a bearer token. Get one from Duende with the
resource-owner-password grant — the same flow the Angular SPA uses (`Frontend/FRONTEND_PLAN.md`):

```bash
curl -s -X POST http://localhost:18080/connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password" \
  -d "client_id=fooddeliveryservice-public-client" \
  -d "username=<email>" \
  -d "password=<password>" \
  -d "scope=openid profile email fooddeliveryservice.api"
```

Paste the `access_token` into Authorize, then call an operation. Notes that save a confused ten
minutes:

- The public client is **secret-less** (`RequireClientSecret = false`) because a public client
  cannot use `client_credentials` — Duende rejects that combination outright.
- Access tokens live **15 minutes** (Milestone E). A UI left open past that starts answering 401;
  re-authorize rather than debugging.
- A 403 after a successful authorize is the normal case, not a bug: the token proves who you are,
  and the permission named on the operation is resolved separately from the Users service. Register
  through `POST users/register` and you are a Customer; staff, managers and drivers are invited.
- Send requests through the Gateway (`:3000`). A service's own port skips the edge rate limiter and,
  in a real deployment, is not reachable at all.

## 4. What is in the document, and where it comes from

One shared `AddApiDocumentation(configuration, ApiDocumentation.{Service})` per host
(`Common.Presentation/Documentation`) replaced seven byte-identical `SwaggerExtensions.cs` copies.
It produces a single document — enriched by three transformers — which both UIs render:

| Part | Source | Written by hand? |
|---|---|---|
| Title, description, contact | `ApiDocumentation.{Service}` | Yes, once per service |
| Bearer security scheme | `ApiDocumentTransformer` | No |
| Operation summary + description | `.WithSummary(…)` / `.WithDescription(…)` on the endpoint | **Yes, per endpoint** |
| Success response + schema | `.Produces<T>()` / `.Produces(Status204NoContent)` | Yes, per endpoint |
| "Requires permission: `orders:manage`" | `AuthorizationOperationTransformer`, read off the endpoint's `RequireAuthorization` policy | No |
| Security requirement, 401, 403 | `AuthorizationOperationTransformer` | No |
| 400 / 404 / 409 / 429 / 500 `ProblemDetails` | `ProblemResponseOperationTransformer`, from the verb and whether the route takes an id | No |

The permission string is **read from the endpoint metadata**, never retyped into prose. That matters
because `EndpointAuthorizationTests` already fails the build when a policy names a permission the
Users module does not seed — so the string the document prints is a string something checked. A
hand-written "requires orders:create" in a summary is checked by nobody.

The failure responses are synthesized rather than written because the error surface is uniform by
construction: every handler returns `Result<T>`, every endpoint calls
`result.Match(Results.Ok, ApiResults.Problem)`, and that one function maps `ErrorType` to
400 / 404 / 409 / 500. Writing those out on fifty endpoints would be fifty chances to write them
differently.

429 is documented on the services even though nothing in a service produces it — the Gateway's edge
rate limiter does ([`rate-limiting.md`](rate-limiting.md)), every client reaches the service through
the Gateway, and the Gateway serves no document of its own, so documenting it there would document
it nowhere.

### One generator, not two

The hosts previously registered **both** `AddOpenApi()` (Microsoft) and `AddSwaggerGen()`
(Swashbuckle) — two independent OpenAPI pipelines building two different documents, of which
Swashbuckle's was served by nothing at all, because `UseSwagger()` was never called anywhere.
`AddSwaggerGen` is gone. Swashbuckle remains for its Swagger UI assets, pointed at the one document
`Microsoft.AspNetCore.OpenApi` produces.

Scalar serves its own bundle from the host (~3.7 MB of `scalar.js` under `/docs/{slug}/scalar/`)
rather than from a CDN, which is what lets the documentation CSP stay at `default-src 'self'`. If a
future package bump reintroduces a CDN default, pin it back with `WithBundleUrl` rather than
widening the CSP.

## 5. The tags convention

Operations are grouped by the tag on the endpoint, and a tag is a `Tags` constant in the endpoint's
own folder — never a string literal at the call site:

```
Modules/{Module}/…Presentation/{Area}/Tags.cs   →   internal const string {Area} = "…";
```

One tag per resource area, and the tag is the sidebar heading a reader scans, so it is named for
what the operations act on rather than for the module: Support's three areas are `Support Tickets`,
`Support Refunds` and `Support Analytics`, because "Tickets" alone would collide in a reader's head
with an order. Delivery's two are `Deliveries` and `Drivers`. `OpenApiDocumentTests` fails an
operation with no tag — an untagged operation lands in a nameless group below every named one, which
is where a reader stops looking.

## 6. Adding an endpoint

The five lines that make the endpoint appear correctly. Four of them fail the build if you skip
them; the fifth is the one to actually think about.

```csharp
app.MapPost("orders/{id:guid}/accept", …)
    .RequireAuthorization(Permissions.ManageOrders)   // EndpointAuthorizationTests
    .WithTags(Tags.Orders)                            // OpenApiDocumentTests
    .WithSummary("Accept an order")                   // OpenApiDocumentTests
    .WithDescription("…")                             // OpenApiDocumentTests — the thinking
    .Produces(StatusCodes.Status204NoContent);        // OpenApiDocumentTests
```

`.Produces<T>()` for an endpoint that returns a body, `.Produces(StatusCodes.Status204NoContent)`
for one that does not — match what `result.Match(…)` actually returns, since nothing infers it from
an `IResult`. Everything else — the bearer requirement, the permission line, 400/401/403/404/409/429/500
— is added for you.

A brand-new **service** needs three more things: a descriptor on `ApiDocumentation`, the
`AddApiDocumentation` / `MapApiDocumentation` pair in its host, and a `docs/{slug}/**` route in
*both* Gateway routing tables. All three are asserted — by `ApiDocumentationCoverageTests` and
`GatewayRouteTests` — so none of them can be the one you forgot.

## 7. Tests

| Test | Asserts |
|---|---|
| `Common.UnitTests/Documentation/OpenApiDocumentTests` | Builds each service's real document in memory and checks every operation has a summary, a description, a tag, a success response, 400/429/500, and — unless it is on the anonymous allow-list — the bearer requirement plus 401/403. Also that each document carries its own title and the bearer scheme, and that every documentation path falls inside the CSP carve-out. |
| `Common.UnitTests/Documentation/ApiDocumentationCoverageTests` | Every module host registers *and* maps the documentation, maps it after `UseAuthentication()`, and every descriptor on `ApiDocumentation.All` is served by some host. |
| `Common.UnitTests/Security/GatewayRouteTests` | Every documented service has a `docs/{slug}/**` route to its **own** cluster, in both the compose table and the Kubernetes ConfigMap, and the anonymous allow-list is exactly the two registration paths plus the documentation routes. |

One gotcha worth knowing before editing `OpenApiDocumentTests`: **the host has to be started.** A
`WebApplication` keeps the endpoint data sources `MapEndpoints` added in its own collection and only
publishes them to the container when it starts, and the document is generated from the container's.
Skip `StartAsync` and every assertion passes over a document with zero paths. Kestrel is bound on
port 0 so that starting seven hosts collides with nothing.

## 8. Known limitations

- **No aggregated document, and no versioning.** One document per service, called `v1`, and nothing
  yet distinguishes a breaking change from an additive one. The route pattern keeps the
  `{documentName}` placeholder so a second document needs no new route.
- **Request examples are not written.** The schemas are generated from the request records, which is
  accurate but not illustrative; nothing here shows a filled-in `PlaceOrder` body.
- **The Gateway and Identity serve no document of their own.** The Gateway has no API — it forwards
  seven — and Identity's surface is its OIDC discovery document at
  `http://localhost:18080/.well-known/openid-configuration`.
- **`Try it` against a real stack needs the whole stack.** An operation call goes through the
  Gateway to a service that wants PostgreSQL, Redis, RabbitMQ and Duende; the documentation renders
  without any of them, but a request will not succeed without all of them.
