# Feature 2.5 — Kubernetes Deployment

> **Status (2026-08-05): built and deployable.** All eight services run on a local Kubernetes
> cluster with plain manifests and `kubectl`. This document describes what exists and why, then
> lists the improvements that were deliberately *not* built.
>
> **Scope was cut on purpose.** An earlier draft of this plan (2026-07-19, revised 2026-08-04)
> specified eight milestones ending in Helm, autoscaling, an NGINX Ingress, a CI deploy pipeline and
> Terraform-provisioned AKS. That was more machinery than this project needs. The goal now is the
> plain one — *the solution deploys to Kubernetes* — and everything beyond it lives in
> §5 as optional work with the reasoning preserved. Nothing in §5 is pending or half-finished; each
> item is a considered "not now".

Operational documentation lives in [`deploy/README.md`](deploy/README.md). This file is the *why*.

---

## 1. What exists

```
deploy/
├── kind/
│   ├── kind-cluster.yaml            # the cluster: 3 nodes, 2 ports published to the host
│   └── scripts/kind-{up,down}.{sh,ps1}
└── k8s/
    ├── base/                        # namespace, shared config + secrets, Postgres, Redis, RabbitMQ
    ├── services/                    # one file per service — Service + Deployment
    └── scripts/
        ├── policy-check.py          # manifest shape gate (no cluster needed)
        └── cluster-smoke.sh         # behavioural gate (needs a deployed cluster)
```

One command — `kind-up.sh` / `kind-up.ps1` — creates the cluster, builds all eight images from the
existing Dockerfiles, loads them onto the nodes, applies everything and waits for each service to
report Ready. The Gateway then answers on `localhost:8000` and Identity on `localhost:18080`, the
same two entry points `docker-compose` publishes.

| Piece | Why it is necessary |
|---|---|
| `kind-cluster.yaml` | A cluster has to come from somewhere. KinD runs Kubernetes nodes as Docker containers — same API, same scheduler, same probe semantics as a managed cluster, but local, free and disposable. |
| `kind-up` / `kind-down` | KinD nodes have **no registry** to pull `fooddeliveryservice/*` from, so every image must be built locally and pushed onto the nodes with `kind load docker-image`. Without this step every pod sits in `ImagePullBackOff`. |
| `base/namespace.yaml` | Everything else lives inside it. |
| `base/config.yaml` | One `ConfigMap` of shared non-secret settings + one `Secret` of credentials. This is what keeps the eight service files short: each pulls the ConfigMap in with `envFrom` and names only what is genuinely its own. It is also why no password appears in a Deployment. |
| `base/{postgres,redis,rabbitmq}.yaml` | The three backing stores, translated from `docker-compose.yml`. Dev-cluster stand-ins, not production data infrastructure. |
| `services/*.yaml` | Eight files, one per host, each a `Service` + a `Deployment`. `orders.yaml` carries the explanatory comments; the five other module hosts are the same file with a different name, image and database key. |
| `scripts/policy-check.py` | Asserts the manifests have the shape decided on, in ~1 second, with no cluster. |
| `scripts/cluster-smoke.sh` | Asserts a *deployed* cluster actually works — including the probe behaviour under a real dependency outage. |

**Deliberately absent:** Helm, Kustomize, Ingress, TLS, autoscaling, PodDisruptionBudgets, a CI
deploy step, Azure, and the in-cluster observability stack. See §5.

---

## 2. Decisions that are load-bearing

These are the ones that cost time to discover. Changing any of them casually will break the
deployment in ways that are slow to diagnose.

- **`ASPNETCORE_ENVIRONMENT=Kubernetes`, not `Development`.** Every real config value in this repo
  lives in `appsettings.Development.json`; `appsettings.json` ships empty placeholders. Running
  non-Development in-cluster is correct, and three behaviours flip with it: the Redis cache/lock
  in-memory fallback switches **off** (an unreachable Redis must fail readiness, not silently become
  a per-pod "distributed" lock), the OpenAPI document is not mapped, and Serilog's **Seq sink
  disappears** — pods log to console only. Because the environment is not Development, ASP.NET
  Identity also applies its real password rules, which is why the seeded admin password is
  `Admin!23456` and not compose's `admin`. Compose's value would fail to seed *silently*, leaving no
  account to log in with.
- **The Gateway needs a mounted config file, not environment variables.** `Gateway/appsettings.json`
  ships an **empty** `ReverseProxy` section — every route and cluster exists only in
  `appsettings.Development.json`. Outside Development the Gateway therefore has no routes at all and
  proxies nothing, and there is nothing for an env-var override to override. `services/gateway.yaml`
  mounts an `appsettings.Kubernetes.json` from a ConfigMap carrying the routing with destinations
  re-pointed at Service names. **A new route prefix must be added in both places.**
- **Service names cannot contain dots.** They are DNS-1035 labels, so the compose hostname
  `fooddeliveryservice.orders.api` becomes `fooddeliveryservice-orders-api`. That rename is the only
  reason the Gateway's destinations needed re-pointing at all.
- **No `ASPNETCORE_HTTPS_PORTS` in a pod.** Compose sets `8081` and mounts a dev certificate; a pod
  that opens an HTTPS port with no certificate fails to start. The Dockerfiles' `EXPOSE 8081` is
  inert here. TLS is a cluster-edge concern (§5).
- **`runAsUser: 1654` is stated explicitly.** `USER $APP_UID` resolves to that UID in
  `mcr.microsoft.com/dotnet/aspnet:10.0`, which is the *only* reason a bare `runAsNonRoot: true`
  passes admission — the kubelet cannot tell whether a symbolic `USER` is root. Stating the number
  makes the guarantee ours rather than the base image's, and gives the policy check something real
  to assert.
- **Redis is configured, not defaulted.** The single instance carries the `IDistributedLock` keys,
  Delivery's driver GEO set and the SignalR backplane as well as the cache. It runs
  `maxmemory-policy noeviction` with an append-only file: an eviction that drops a lock key is a
  correctness bug that presents as a race, and a restart that loses the GEO set loses every driver
  position. It stays a **single logical instance** — clustering it would break the lock's guarantee.
- **Identity is a readiness dependency of all six module hosts.** Their `Duende` health check probes
  its aggregate `/health`, so an Identity outage takes all six unready at once. That correlated
  failure is intended (a service that cannot resolve permissions cannot serve authenticated
  traffic) and it is why `kind-up` waits for Identity *before* waiting on anything else — otherwise
  every rollout times out together and the cause is invisible.
- **Identity needs a writable `/app/keys`.** Duende's automatic key management writes signing keys
  under the working directory, which the non-root user cannot create inside the image. An `emptyDir`
  makes startup work; keys regenerate on restart, invalidating previously issued tokens. Fine
  locally, not fine anywhere real.
- **One replica per service.** Every module host runs `app.ApplyMigrations()` at startup, so a
  second pod is two processes racing to migrate the same database. RealTime would need pinning
  regardless — see §5.
- **`OTEL_SDK_DISABLED=true`.** There is no OpenTelemetry Collector in the cluster. Left enabled,
  the OTLP exporter retries a dead endpoint every few seconds and buries the startup log. The
  observability stack remains a `docker-compose` concern.
- **The committed secrets are real values, and that is fine.** They are only ever valid against a
  throwaway local cluster. A real environment replaces the `Secret`'s *contents*; the **keys** stay
  the same, so no Deployment changes.

---

## 3. How it is checked

| Gate | Command | Catches | Runs |
|---|---|---|---|
| Schema | `kubeconform -strict deploy/k8s` | invalid Kubernetes YAML | every PR |
| Shape | `python3 deploy/k8s/scripts/policy-check.py deploy/k8s` | `:latest` or untagged images; missing resource requests/limits; a credential pasted as a literal instead of a `secretKeyRef`; a missing or wrong probe path; a stray `ASPNETCORE_HTTPS_PORTS`; a missing `ASPNETCORE_ENVIRONMENT` | every PR |
| Behaviour | `deploy/k8s/scripts/cluster-smoke.sh` | all eight Ready; Gateway proxies downstream; Identity serves discovery; **the probe split under a real outage** | push to `development`/`main`, or on demand |

The third is the one that matters. It scales Redis to zero and asserts that Orders'
`/health/ready` returns `503` while `/health/live` stays `200`, that the pod leaves the Service
endpoints, and that it is **not restarted** — then restores Redis and watches readiness recover on
its own. Feature 2.4 built that liveness/readiness split; this is the first place a kubelet is
actually acting on it, which is the only place it can be observed.

The smoke test is excluded from per-PR runs because it builds eight .NET images. The two fast gates
already cover the manifests themselves.

---

## 4. What this deployment is not

Stated plainly so nobody mistakes the local cluster for a production posture:

- **Not highly available.** Single replica per service, single Postgres, single Redis, single
  RabbitMQ, no PodDisruptionBudgets. A node drain takes services down.
- **Not TLS-terminated.** Traffic reaches the Gateway over plain HTTP on a NodePort.
- **Not autoscaling.** Fixed replica counts; no metrics-server, no HPA.
- **Not backed up.** The PersistentVolumeClaims live on KinD's local-path provisioner and disappear
  with the cluster.
- **Not observable in-cluster.** Telemetry export is off; Jaeger/Prometheus/Grafana/Seq remain a
  compose-only story.
- **Not automatically deployed.** CI validates the manifests and smoke-tests a cluster; it does not
  publish images or roll anything out.

---

## 5. Optional improvements for later

Ordered roughly by value per unit of effort. Each is independent; none is a prerequisite for the
current deployment being correct. The traps listed inside them are findings from a codebase audit —
they are the expensive part, and they are recorded here so they do not have to be rediscovered.

### 5.1 Make it safe to run more than one replica *(the highest-value item)*

Three separate hazards, all cheap to fix, all invisible until a second pod exists:

1. **Migration race.** Six module hosts call `app.ApplyMigrations()` at startup; Identity does the
   equivalent inline with `ApplyDatabaseMigrationsAsync` + `AdminSeeder`. Gate it behind
   `Database:RunMigrationsOnStartup` (default `true` so compose and the integration suites are
   unchanged, `false` in-cluster) and run migrations once from a one-shot `Job` before the new pods
   roll. Prefer one shared extension over seven copies.
2. **`FOR UPDATE` without `SKIP LOCKED`.** `ProcessOutboxJob` and `ProcessInboxJob` select a batch
   `FOR UPDATE` and then dispatch the *entire batch* — handler plus MassTransit publish — inside
   that transaction. Quartz's `[DisallowConcurrentExecution]` is per-scheduler, i.e. **per pod**.
   Correctness holds (under READ COMMITTED, Postgres re-evaluates the `WHERE` after the lock is
   released, so nothing is dispatched twice), but replica two blocks on replica one for a whole
   batch. Adding `SKIP LOCKED` — one word, six modules — makes replicas take disjoint batches.
   Without it, scaling out makes outbox latency *worse*.
3. **Delivery's expired-offer counter double-counts.** `ProcessExpiredOffersJob` takes no
   distributed lock, so every replica scans the same expired offers.
   `DeliveryAssignmentDiagnostics.RecordExpiredOffer()` fires at *detection*, before the command, so
   the metric multiplies by replica count. State stays correct — the 2.3 lock and the aggregate
   guard make the losers fail harmlessly — but a dashboard that lies under load is worse than no
   dashboard. Move the counter after a successful command.

**Not a hazard — do not "fix" it:** MassTransit consumer queues are already replica-safe.
`instanceId` derives from the constant service name, so all replicas are **competing consumers on
one queue**. Making the instance id per-pod would give every replica a copy of every event.

### 5.2 An Ingress with TLS

Replace the two NodePorts with `ingress-nginx` terminating TLS in front of the Gateway, plus a
dedicated path for Identity's discovery/token endpoints (it is not behind the Gateway today).
`hubs/**` needs raised `proxy-read-timeout`/`proxy-send-timeout` for long-lived WebSockets.

**The trap:** Identity's issuer has to survive the boundary. `IdentityServer:IssuerUri` is the
internal Service name today, and every host validates against it. Once Identity is publicly
reachable, tokens carry either the internal issuer (clients validating against the public discovery
document reject them) or the public one (in-cluster validation rejects them). The resolution — pin
`IssuerUri` to the **public** URL, list **both** values in every service's `ValidIssuers`, keep
`MetadataAddress` on internal DNS so key fetches never leave the cluster — is cheap up front and
costs a day of confusing 401s if discovered afterwards.

### 5.3 Templating, if the manifest duplication starts to hurt

Eight near-identical Deployments differing in about six values. Two options, in ascending cost:
**Kustomize** (already built into `kubectl` — a base plus small per-service patches, no templating
language) or **Helm** (charts, values files, releases, `helm rollback`; worth it for multiple
environments or software other people install). Neither is needed at this size — the shared
ConfigMap/Secret already removed most of the duplication.

Note for whenever this happens: Feature 2.6 adds a **ninth** host
(`FoodDeliveryService.Reviews.Api`, database `fooddeliveryservice_reviews`, a `reviews/**` route).
Under the current scheme that is one new file plus one Gateway destination; any templating that
makes it *harder* than that is the wrong templating.

### 5.4 Autoscaling and zero-downtime rollouts

Requires §5.1 first — scaling is unsafe *and* counter-productive without it. Then: metrics-server, a
CPU-target `HorizontalPodAutoscaler` on Orders and Delivery, `PodDisruptionBudget`s, and
`RollingUpdate` with `maxUnavailable: 0`.

**Two exclusions to honour.** **RealTime must stay at one replica.** The Redis backplane distributes
*messages* between instances; it does not make SignalR's *negotiate* handshake portable.
`POST hubs/tracking/negotiate` lands on pod A and returns a connection token only A knows; the
WebSocket connect that follows is a separate TCP connection kube-proxy may route to pod B, which
rejects it. This cannot be fixed at the Ingress either — the Ingress fronts the *Gateway*, and YARP
addresses RealTime as a single Service, so YARP's session affinity has no destinations to
affinitize between. `sessionAffinity: ClientIP` on the RealTime Service is a partial guard (it pins
per *Gateway* pod, not per client). The real scale path is **Azure SignalR Service**. Second: the
**Gateway** can scale freely today only because Feature 1.3's rate limiter was never built — if one
is ever added it must be Redis-backed, or per-pod buckets multiply the limit by the replica count.

### 5.5 The observability stack in-cluster

Run 2.4's Collector, Prometheus, Grafana, Jaeger, Seq as cluster workloads and delete
`OTEL_SDK_DISABLED`. Also supply the `Serilog` section from a mounted `appsettings.Kubernetes.json`
so logs reach Seq again — outside Development the sink does not exist, so this is real work, not a
re-host.

**The trap:** `docker/prometheus/prometheus.yml` scrapes **static compose hostnames** — the
collector plus sixteen blackbox targets (`/health/live` + `/health/ready` × 8 hosts) — and derives
the `service` label by regex from those hostnames. Point it at Kubernetes Service DNS as-is and each
probe hits one arbitrary pod while every dashboard's `service=` filter goes blank. It needs
`kubernetes_sd_configs` with labels re-derived from pod metadata, and `ObservabilityAssetTests`
fails the build if dashboards and emitted metric names drift apart — so the relabelling and the
dashboards move together.

### 5.6 CI/CD and a registry

Extend `.github/workflows/ci.yml` to build all eight images on push, tag them with the git SHA
(never `latest`), push to GHCR, and deploy. This also removes the `kind load` step from the
deployment path, and would let the integration suites currently skipped in CI — the ones needing
Identity on `:18080` — run against a real in-cluster Identity.

### 5.7 Azure (AKS)

`deploy/infra/` with Terraform or Bicep: AKS, ACR, Key Vault + the Secrets Store CSI driver behind
the same env keys, Azure Database for PostgreSQL, Azure Cache for Redis (still a **single** instance,
for the lock), cert-manager for real certificates. There is no existing Azure footprint to extend —
Feature 1.7 was specified but never built, so this would create ACR and Key Vault for the first
time.

### 5.8 Explicitly not worth it here

Service mesh / pod-to-pod mTLS (cross-service traffic already goes over the bus or the Gateway);
GitOps reconcilers; KEDA queue-depth autoscaling; self-hosted Postgres/RabbitMQ HA operators with
PITR backups; secret-rotation automation; multi-region, blue-green and canary deploys.

---

## 6. Relationship to the other features

- **Telemetry (2.4)** produced `/health/live` + `/health/ready`
  ([`docs/health-probe-contract.md`](docs/health-probe-contract.md)). The probes here bind to them
  verbatim — no health endpoints were authored for Kubernetes. Its observability backends are §5.5.
- **Real-Time (2.2)** produced the RealTime host and `hubs/**`. It is the eighth Deployment, pinned
  to one replica for the SignalR reason in §5.4.
- **Caching (2.3)** requires a single logical Redis for `IDistributedLock`. That constraint shapes
  `base/redis.yaml` — single StatefulSet, `noeviction` — and survives into any Azure move.
- **Reviews (2.6)** will add a ninth host; under the current scheme that is one manifest file plus
  one Gateway destination.
- **CI/CD (1.7)** was never implemented. `.github/workflows/ci.yml` — build, test, and the manifest
  gates above — is the project's first pipeline. Its deploy half is §5.6.

---

## 7. Repo facts worth keeping

- Solution targets **.NET 10**; images are `mcr.microsoft.com/dotnet/{sdk,aspnet}:10.0`, in which
  `$APP_UID` is **1654**.
- All eight Dockerfiles exist, are multi-stage and already run as a non-root `USER $APP_UID`.
- The Gateway has **no rate limiter** — a Feature 1.3 task that was never built.
- A pre-existing Gateway defect was fixed alongside the first CI workflow: three routes referenced a
  `fooddeliveryservice-cluster` that was never defined. The dead catch-all route was deleted and the
  two anonymous routes (`users/register`, `users/accept-invitation`) repointed at
  `fooddeliveryservice-users-cluster`.
