# `deploy/` — running the solution on Kubernetes

All nine services (Gateway, Identity, Users, Orders, Restaurants, Delivery, Notifications,
RealTime, Support) plus PostgreSQL, Redis and RabbitMQ, deployed to a local Kubernetes cluster with plain
manifests and `kubectl`. No Helm, no Kustomize, no extra tooling to learn.

```
deploy/
├── kind/
│   ├── kind-cluster.yaml            # the cluster: 3 nodes, 2 ports published to your machine
│   └── scripts/kind-{up,down}.{sh,ps1}
└── k8s/
    ├── base/                        # namespace, shared config + secrets, Postgres, Redis, RabbitMQ
    ├── services/                    # one file per service — Service + Deployment
    └── scripts/
        ├── policy-check.py          # checks the manifests' shape (no cluster needed)
        └── cluster-smoke.sh         # checks a deployed cluster actually works
```

## Run it

Needs [Docker](https://www.docker.com/), [kind](https://kind.sigs.k8s.io/) and `kubectl`.

```bash
Backend/deploy/kind/scripts/kind-up.sh
```

```powershell
Backend\deploy\kind\scripts\kind-up.ps1
```

Creates the cluster, builds all nine images from the existing Dockerfiles, loads them onto the
nodes, applies everything and waits until each service reports Ready. First run is roughly 10–15
minutes, nearly all of it image builds; `--no-build` / `-NoBuild` redeploys in seconds using the
images already on the nodes.

Then, exactly like compose:

| | |
|---|---|
| Gateway | <http://localhost:8000> (compose's `:3000`) |
| Identity | <http://localhost:18080> |

`kind-down` deletes the cluster and everything in it.

Everything else is ClusterIP — reachable only from inside the cluster, so all external traffic goes
through the Gateway, unchanged from the compose topology. To reach a service directly for debugging:

```bash
kubectl -n fooddeliveryservice port-forward svc/fooddeliveryservice-orders-api 5200:8080
```

## How the files fit together

**`base/config.yaml` is where the configuration lives.** One `ConfigMap` holds what every host
shares (environment name, HTTP port, JWT validation, Identity's health URL) and one `Secret` holds
the credentials (connection strings, the client secret, the admin password). Each Deployment pulls
the ConfigMap in wholesale with `envFrom` and names only the handful of settings that are genuinely
its own — which is why the nine service files are short and nearly identical.

**`services/orders.yaml` is the one to read.** The other six module hosts are the same file with a
different name, image and database key, and say so in their header. Only three files differ in
substance: `users.yaml` (extra Duende settings — it is the only service that calls another over
HTTP), `identity.yaml` (issues the tokens, published on 18080) and `gateway.yaml` (the routing
table, published on 8000).

## Things that are deliberate

- **`ASPNETCORE_ENVIRONMENT=Kubernetes`, not Development.** Three behaviours flip and all three are
  wanted: the Redis cache/lock in-memory fallback switches off (an unreachable Redis must fail
  readiness rather than silently become a per-pod "distributed" lock), the OpenAPI document is not
  mapped, and Serilog's Seq sink — which only exists in `appsettings.Development.json` — is absent,
  so pods log to console. It also means Identity applies ASP.NET Identity's real password rules,
  which is why the seeded admin password is `Admin!23456` and not compose's `admin`.
- **The Gateway gets a mounted `appsettings.Kubernetes.json`, not environment variables.** Its
  `appsettings.json` ships an **empty** `ReverseProxy` section — every route and cluster lives in
  the Development file — so outside Development the Gateway has no routes at all and proxies
  nothing. There is nothing for an environment variable to override, hence the small config file in
  `services/gateway.yaml`. If you add a route prefix, add it in both places.
- **HTTP only inside the pod.** No `ASPNETCORE_HTTPS_PORTS` and no dev-certificate mount: a pod that
  opens an HTTPS port with no certificate fails to start. The Dockerfiles' `EXPOSE 8081` is inert.
- **Redis is configured, not defaulted.** It carries the distributed lock keys, Delivery's driver
  GEO set and the SignalR backplane as well as the cache, so it runs `maxmemory-policy noeviction`
  with an append-only file. An eviction that drops a lock key is a correctness bug that reads as a
  race.
- **One replica per service.** Every module host runs `app.ApplyMigrations()` at startup, so a
  second pod is two processes racing to migrate the same database. RealTime would need pinning to
  one anyway: SignalR's negotiate and connect are separate connections that can land on different
  pods, and the Redis backplane does not fix that.
- **Telemetry export is off (`OTEL_SDK_DISABLED=true`).** There is no OpenTelemetry Collector in
  this cluster; left on, the exporter retries a dead endpoint every few seconds and buries the log.
  The observability stack stays a docker-compose concern.
- **The committed secrets are real, and that is fine.** They are only ever valid against a throwaway
  local cluster. A real environment replaces the `Secret`'s contents — the *keys* stay the same, so
  no Deployment changes.

## Checking it

| Check | Command | What it catches |
|---|---|---|
| Schema | `kubeconform -strict deploy/k8s` | invalid Kubernetes YAML |
| Shape | `python3 deploy/k8s/scripts/policy-check.py deploy/k8s` | `:latest` images, missing resource limits, a credential pasted as a literal, a missing or wrong probe path, a stray `ASPNETCORE_HTTPS_PORTS` |
| Behaviour | `deploy/k8s/scripts/cluster-smoke.sh` | all nine Ready; the Gateway proxies downstream; Identity serves discovery; and the probe split under a real outage |

That last one is the interesting one. It scales Redis to zero and asserts that Orders'
`/health/ready` goes `503` while `/health/live` stays `200`, that the pod leaves the Service
endpoints, and that it is **not** restarted — then brings Redis back and watches readiness recover
on its own. The liveness/readiness split only means something once a kubelet is acting on it, and
that is where it is observed.

The first two run on every pull request. The smoke test runs on pushes to `development`/`main` and
on demand — it builds nine .NET images, so it is too slow for per-PR feedback.
