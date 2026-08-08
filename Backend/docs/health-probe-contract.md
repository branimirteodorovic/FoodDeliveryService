# Health probe contract

> Delivered by **Feature 2.4 — Distributed Telemetry & Observability, Milestone C**
> (`TELEMETRY_PHASE2_PLAN.md` §4). Consumed by **Feature 2.5 (AKS)**, which binds pod probes to
> these endpoints, and **Feature 2.6 (Reviews)**, which stands up a new host and copies this
> convention. Everything here is asserted by tests — see [Tests](#tests).

## Endpoints

All **nine** hosts (Gateway, Identity, Notifications, Orders, Restaurants, Users, Delivery,
RealTime, FraudDetection) expose the same three endpoints, mapped by the single shared
`app.MapHealthProbes()` extension in
[`Common.Presentation/Health`](../src/Common/FoodDeliveryService.Common.Presentation/Health/HealthProbeEndpointExtensions.cs).

| Endpoint | Selects | Bind to | Meaning |
|---|---|---|---|
| `GET /health/live` | checks tagged `live` | `livenessProbe` | The process is up and answering. Never fails on a dependency. |
| `GET /health/ready` | checks tagged `ready` | `readinessProbe` | Every external dependency is reachable. |
| `GET /health` | **all** checks, unfiltered | nothing — humans and dashboards | The pre-existing aggregate, unchanged. |

**Status codes:** on the two **probes**, `200` for `Healthy` and `503` for `Degraded` or
`Unhealthy`. A probe is a binary signal to a kubelet, so anything short of healthy must read as "do
not send me traffic"; the ASP.NET Core default maps `Degraded` to `200`, which would leave a degraded
pod in rotation, so `MapHealthProbes` overrides it. The **aggregate** `/health` deliberately keeps
the framework defaults (`Degraded` → `200`) so it stays byte-for-byte what it was before the split.

All three render the same HealthChecks.UI JSON payload:

```json
{ "status": "Healthy", "totalDuration": "...", "entries": { "self": { "status": "Healthy", ... } } }
```

Probes must key on the **status code**, not the body.

## Tag semantics

`HealthCheckTags.Live` / `HealthCheckTags.Ready` (`"live"` / `"ready"`) are the only two tags.

- **`live`** — carried by exactly one check, the dependency-free `self` check registered by
  `AddLivenessCheck()`. A liveness failure restarts the container, so nothing that an outage
  elsewhere can break may enter this set: restarting a pod does not bring PostgreSQL back, it just
  adds a crash-loop to the incident.
- **`ready`** — carried by every external-dependency check. A readiness failure pulls the pod out
  of the load-balancer rotation but leaves it running, so it rejoins by itself when the dependency
  recovers.

A check may carry both (the Gateway does — see below). No check may carry only `live` except
`self`.

## Per-host check sets

| Host | `live` | `ready` |
|---|---|---|
| Notifications, Orders, Restaurants, Users, Delivery, RealTime, FraudDetection | `self` | `npgsql`, `redis`, `rabbitmq`, `Duende`, `masstransit-bus` |
| Identity | `self` | `npgsql` |
| Gateway | `self` | `self` |

Notes:

- **`masstransit-bus`** is registered and tagged `ready` by MassTransit itself, not by the host. It
  belongs in the set on its own merits — a bus that has not connected means the service can neither
  publish nor consume — and it is asserted so that a MassTransit upgrade changing the tag is caught.
- **`Duende` is deliberately in `ready`.** The six module hosts probe Identity's aggregate
  `GET /health` as a readiness dependency, so an Identity outage takes all six unready at once.
  That correlated failure is intentional and **2.5 must plan for it**: a service that cannot
  resolve permissions genuinely cannot serve authenticated traffic, and the previous untagged
  `/health` only hid the dependency rather than removing it. If rollout experience shows this is
  too aggressive, the fix is to move the check to a third informational tag — not to loosen the
  meaning of `ready`.
- **The Gateway's readiness equals its liveness.** Its `self` check carries both tags. The obvious
  readiness candidate — "are the downstream clusters up?" — is exactly what YARP exists to degrade
  around; one dead cluster must not take the single public entry point out of rotation and every
  other service with it. Downstream health is each service's own `/health/ready` to report.

## Adding a new host (Feature 2.6 and later)

```csharp
using FoodDeliveryService.Common.Presentation.Health;

builder.Services.AddHealthChecks()
    .AddLivenessCheck()                                                  // the "live" self check
    .AddNpgSql(databaseConnectionString, tags: [HealthCheckTags.Ready])
    .AddRedis(redisConnectionString, tags: [HealthCheckTags.Ready])
    .AddRabbitMQ(sp => sp.GetRequiredService<IConnection>(), tags: [HealthCheckTags.Ready])
    .AddDuende(duendeHealthUrl);                                         // tags itself "ready"

// after builder.Build()
app.MapHealthProbes();
```

Every dependency check **must** be tagged `ready` — an untagged check appears only in the aggregate
`/health` and is invisible to both probes, which is a silent hole rather than a loud failure. Hosts
that do not use `Common.Infrastructure` (Gateway, Identity) reference `Common.Presentation` for the
probe contract alone.

## Kubernetes wiring (for Feature 2.5)

```yaml
livenessProbe:
  httpGet: { path: /health/live, port: http }
readinessProbe:
  httpGet: { path: /health/ready, port: http }
```

All three endpoints are mapped `.AllowAnonymous()`, so the kubelet — which carries no token — reaches
them regardless of a host's authorization defaults. On the Gateway they are mapped before
`MapReverseProxy()` and no YARP route matches `/health`, so they are served locally and never proxied
downstream.

## Tests

| Level | Where |
|---|---|
| Tag predicates select the expected check sets; `self` is healthy and dependency-free; the Gateway's both-tags shape | [`Common.UnitTests/Health/HealthProbeTests.cs`](../src/Common/FoodDeliveryService.Common.UnitTests/Health/HealthProbeTests.cs) |
| Real host, real dependencies: all three probes `200`; `live` covers only `self`; `ready` covers every dependency; **a downed dependency → `ready` 503 while `live` stays 200** | [`Orders.IntegrationTests/Health/HealthProbeTests.cs`](../src/Modules/Orders/FoodDeliveryService.Modules.Orders.IntegrationTests/Health/HealthProbeTests.cs) |
