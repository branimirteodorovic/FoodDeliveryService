# Metrics backend, dashboards and alerts

> Delivered by **Feature 2.4 — Distributed Telemetry & Observability, Milestone E**
> (`TELEMETRY_PHASE2_PLAN.md` §6). Consumed by **Feature 2.5 (AKS)**, which runs the same four
> containers as in-cluster workloads and points every pod's `OTEL_EXPORTER_OTLP_ENDPOINT` at the
> in-cluster collector. The health probes these alerts are built on are
> [`docs/health-probe-contract.md`](health-probe-contract.md); the cache counters they read are
> [`docs/caching.md`](caching.md).

Milestones A and B instrumented every host — RED at the transport and application boundaries,
business counters on the order and assignment flows, cache hit/miss on every lookup — and exported
all of it over OTLP to **Jaeger**, which accepts traces and drops metrics on the floor. This
milestone is the other end of that pipe.

## 1. What runs, and where

| Container | Image | Host port | Role |
|---|---|---|---|
| `fooddeliveryservice.otel-collector` | `otel/opentelemetry-collector-contrib:0.157.0` | `4317` OTLP/gRPC, `4318` OTLP/HTTP, `8889` exposition | The single endpoint every service exports to; fans traces to Jaeger and metrics to Prometheus |
| `fooddeliveryservice.prometheus` | `prom/prometheus:v2.55.1` | `9090` | Scrapes the collector and the blackbox probes; evaluates the alert rules |
| `fooddeliveryservice.grafana` | `grafana/grafana:11.4.0` | `3100` | Dashboards; datasources and dashboards are provisioned from disk |
| `fooddeliveryservice.blackbox` | `prom/blackbox-exporter:v0.25.0` | `9115` | Probes each host's `/health/live` and `/health/ready` from outside the process |
| `fooddeliveryservice.jaeger` | unchanged | `16686` (UI only) | Traces, as before — it no longer publishes `4317`/`4318`, the collector does |
| `fooddeliveryservice.seq` | unchanged | `8081` | Logs, as before — Serilog writes to it directly |

```
 8 hosts ──OTLP:4317──▶ Collector ──┬─ traces  ──▶ Jaeger  :16686
                                    ├─ metrics ──▶ :8889 ◀──scrape── Prometheus :9090 ──▶ Grafana :3100
                                    └─ logs    ──▶ Seq (wired, idle — see §6)
                                                   blackbox :9115 ◀──scrape──┘
 8 hosts ──Serilog────▶ Seq :8081
```

Everything is config. The only application-side change this milestone makes is the value of
`OTEL_EXPORTER_OTLP_ENDPOINT` in the eight `appsettings.Development.json` files, which now reads
`http://fooddeliveryservice.otel-collector:4317`. A regression test pins it
(`ObservabilityAssetTests.Host_Should_ExportOtlpToTheCollector_NotStraightToJaeger`) — a new host
copied from an old one and left pointing at Jaeger would emit metrics nothing collects, which is the
exact hole this milestone closed.

## 2. How an instrument becomes a Prometheus series

The collector's Prometheus exporter renames on the way out, and every dashboard query and alert
expression is written against the **right-hand** column. This is the table to check first when a
panel is empty.

| Instrument (OTLP) | Unit | Prometheus | Owner |
|---|---|---|---|
| `http.server.request.duration` | `s` | `http_server_request_duration_seconds_{bucket,sum,count}` | ASP.NET Core instrumentation (A) |
| `app.requests` | `{request}` | `app_requests_total` | `ApplicationDiagnostics` (B) |
| `app.request.duration` | `s` | `app_request_duration_seconds_{bucket,sum,count}` | `ApplicationDiagnostics` (B) |
| `app.request.failures` | `{request}` | `app_request_failures_total` | `ApplicationDiagnostics` (B) |
| `orders.placed` | `{order}` | `orders_placed_total` | `OrdersDiagnostics` (B) |
| `orders.state_transition` | `{transition}` | `orders_state_transition_total` | `OrdersDiagnostics` (B) |
| `delivery.assignment.outcome` | `{assignment}` | `delivery_assignment_outcome_total` | `DeliveryAssignmentDiagnostics` (B) |
| `delivery.assignment.duration` | `s` | `delivery_assignment_duration_seconds_{bucket,sum,count}` | `DeliveryAssignmentDiagnostics` (B) |
| `cache.hits` / `cache.misses` | `{lookup}` | `cache_hits_total` / `cache_misses_total` | `CacheDiagnostics` (Caching 2.3 E) |
| — | — | `probe_success`, `probe_duration_seconds` | blackbox exporter (this milestone) |

Rules: dots become underscores, a monotonic counter gains `_total`, a histogram in seconds gains
`_seconds`, and a unit in braces is an annotation that is **dropped** rather than turned into a
suffix. Tag keys are renamed the same way — `error.type` → `error_type`, `cache.key_prefix` →
`cache_key_prefix`.

Two labels come from the OTel **resource** rather than from a tag:

- **`service_name`** — every panel groups by it. It exists because the collector's exporter runs with
  `resource_to_telemetry_conversion.enabled: true`; without that, the service dimension would live
  only on a separate `target_info` series and nothing here could group by it.
- **`service_instance_id`** — a GUID regenerated per process start. It is kept so two replicas of one
  service do not collide into a single duplicate series.

The `otel-collector` scrape job sets **`honor_labels: true`**, so the `job` and `instance` labels the
collector republishes (from `service.name` and `service.instance.id`) survive instead of being
renamed `exported_job` / `exported_instance` and replaced by the collector's own.

## 3. Dashboards

Provisioned from `docker/grafana/dashboards/*.json`, in the **FoodDeliveryService** folder. Their
uids are fixed in the files, so links to them are stable.

| Dashboard | uid | Answers |
|---|---|---|
| **RED** | `fds-red` | Rate, errors and duration per service, at both layers — the transport histogram (what the caller saw) and the application histogram (what the handler pipeline did). Plus the two probe counts, so "is anything down" is on the same screen. |
| **Business** | `fds-business` | Orders per minute, the full lifecycle transition graph tagged `from`→`to`, cancellation share, and the driver-offer outcome mix with its p95 duration. |
| **Cache** | `fds-cache` | Hit rate overall, per key prefix and for `user_permissions` specifically, plus lookups and misses per service. |

Panels worth knowing about:

- **RED, "Error % by service"** counts 5xx only. A rejected command that correctly answers 400 is the
  system working; those are counted separately on the application row, as failure `Result`s.
- **RED, "Slowest requests"** measures the pipeline including cache hits — `RequestMetricsBehavior`
  sits *outside* `QueryCachingBehavior`, so the line describes every call rather than only the misses.
- **Business, assignment panels** — `delivery.assignment.outcome` and `.duration` **do not share a
  denominator**. The counter also carries `expired`, recorded by the expiry job for a *previous*
  offer. Read the counter per outcome, never as a total. The dashboard says so in a text panel.
- **Cache** — in Development an unreachable Redis degrades to an in-process cache, which the counters
  happily report a healthy hit rate against. A high hit rate is **not** evidence that Redis is up;
  `/health/ready` is.

## 4. Alerts

Prometheus rules in `docker/prometheus/rules/alerts.yml`, visible at
<http://localhost:9090/alerts> and under Grafana's *Alerting → Alert rules*. There is deliberately no
Alertmanager (§6).

| Alert | Fires when | For | Severity |
|---|---|---|---|
| `ServiceDown` | a host fails its `/health/live` probe | 1m | critical |
| `ServiceNotReady` | `/health/ready` fails **while** `/health/live` still passes | 2m | warning |
| `HighHttpErrorRate` | 5xx > 5% of requests for a service | 2m | critical |
| `HighApplicationExceptionRate` | requests that *threw* > 5% for a service | 2m | critical |
| `HighHttpLatencyP95` | transport p95 > 1s | 5m | warning |
| `SlowApplicationRequests` | application p95 > 1s for a given request type | 5m | warning |

Two decisions behind them:

- **Availability is measured from outside.** A killed container reports no error rate — it reports
  nothing, which is exactly what an idle container reports. `probe_success` is what makes "kill a
  service and watch an alert fire" true without traffic having to be flowing at the time. That is
  also why the blackbox exporter is here at all, and it consumes Milestone C's contract directly.
- **Every ratio is guarded by a minimum request rate** (`> 0.05` req/s). Without it, one failed
  request on a quiet night is a 100% error rate and every ratio alert here would be permanently
  firing, which is the same as having none.

`ServiceNotReady` intentionally does *not* fire for a host that is entirely gone — its
`and on (service) probe_success{job="health-live"} == 1` guard leaves that case to `ServiceDown`, so
one outage produces one alert rather than two.

## 5. Manual smoke check

The milestone is infrastructure, so this is the acceptance test. It takes about five minutes.

```bash
docker-compose up -d
```

1. **Collector is receiving.** `curl http://localhost:8889/metrics | grep app_requests_total` — once
   any request has gone through a host, this returns series carrying
   `service_name="fooddeliveryservice.orders.api"` and friends. An empty result means the services are
   still exporting to Jaeger; check `OTEL_EXPORTER_OTLP_ENDPOINT`.
2. **Prometheus is scraping.** <http://localhost:9090/targets> — `otel-collector` **up**, and the
   sixteen `health-live` / `health-ready` probes **up**.
3. **Drive some traffic.** Register a user and place an order through the gateway on `:3000` (the
   `.http` files next to each host work), then hit a few reads so the caches warm.
4. **RED populates.** <http://localhost:3100/d/fds-red> — request rate, error % and the latency
   percentiles all draw. The "Hosts answering /health/live" stat reads **8**.
5. **Business populates.** <http://localhost:3100/d/fds-business> — orders/min moves, and the
   transitions panel shows `none → Pending` followed by whatever the order did next.
6. **Cache populates.** <http://localhost:3100/d/fds-cache> — `user_permissions` climbs toward a high
   hit rate as soon as the same token is used twice.
7. **An alert fires.** `docker stop FoodDeliveryService.Orders.Api`, wait ~1 minute, and
   <http://localhost:9090/alerts> shows **ServiceDown** firing with
   `service="fooddeliveryservice.orders.api"`. `docker start FoodDeliveryService.Orders.Api` clears
   it within a scrape or two.
8. **A dependency outage is distinguishable.** `docker stop FoodDeliveryService.Redis` instead:
   `ServiceDown` stays quiet and **ServiceNotReady** fires for the hosts whose readiness check now
   fails — the liveness/readiness split doing exactly what Feature 2.5 will bind pod probes to.

Traces are unaffected by all of this: Jaeger is still at <http://localhost:16686>, it just receives
them via the collector now.

## 6. Deliberately not here

- **Alertmanager.** Routing, grouping and silencing belong with a real environment and a real
  on-call. Locally, a firing rule in Prometheus and in Grafana's alerting view is the whole
  notification story worth having.
- **OTLP logs.** The collector has a logs pipeline wired to Seq's OTLP ingest endpoint, but
  `AddHostTelemetry(exportLogsViaOtlp:)` stays **false**: Serilog → Seq is the primary log path and
  the one carrying Milestone D's correlation enrichment, so turning the flag on would write every log
  line to Seq twice. The destination exists so that flipping it is a one-line change with somewhere
  real to land, not an invention for later.
- **Exemplars** (click a latency spike, land on the trace). They need the .NET SDK's exemplar filter
  turned on as well as collector and Grafana wiring; the correlation id from Milestone D already
  gets a human from a log line to a trace.
- **Tail-based sampling.** Always-on sampling is kept for a portfolio system. It is a collector
  config change if that ever stops being true.
- **Collector self-telemetry.** Nothing here alerts on the collector itself; if it is down, every
  panel is empty, which is not a subtle failure.
- **Recording rules.** The queries are cheap at this cardinality; precomputing them would add a layer
  to keep in step with the dashboards for no benefit yet.

## 7. Tests

`Common.UnitTests/Observability/ObservabilityAssetTests` parses every asset in `docker/` and pins the
things that fail silently:

- each dashboard is valid JSON with a fixed uid, a title and panels;
- every panel names the **provisioned** Prometheus datasource uid, and every target has a query;
- every metric name referenced by a dashboard **or** an alert is one the code actually emits — the
  guard against a renamed instrument turning into an empty panel and a permanently silent alert;
- the alert file parses, and every rule carries a `for`, a `severity` and a summary/description;
- the dashboard provider's path is the path `docker-compose.yml` mounts;
- all nine hosts export OTLP to the collector.

The instrument names `Common` owns (`app.*`, `cache.*`) are additionally read off the **real** meters
through a `MeterListener`, so the allow-list cannot drift from the code. The Orders and Delivery
names are listed rather than reflected — `Common.UnitTests` references no module, by the same
convention that keeps `{Module}.UnitTests` on its own Domain — and the integration suites in those
modules assert those instruments still export.

Beyond the suite, the assets were validated against the real tooling while building this milestone:
`otelcol validate` on the collector config, `promtool check config` on the Prometheus config and rule
file, `blackbox_exporter --config.check`, and a live run of collector + Prometheus + blackbox +
Grafana in which a synthetic OTLP payload was pushed through the pipe to confirm the name and label
translations in §2 exactly as documented.
