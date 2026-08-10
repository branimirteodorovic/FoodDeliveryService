# Load testing

The k6 harness for **Feature 3.5 — Load Testing & Scalability Demonstration**
(`../LOADTESTING_PHASE3_PLAN.md`). This document covers **Milestone A**: the foundation and the smoke
test. Milestone D expands it into the full runbook (profiles, the breaking-point method, what to
watch while a run is in flight).

## Run it

The stack must already be up (`docker-compose up -d` from `Backend/`). Then, from `Backend/`:

```bash
docker compose --profile loadtest run --rm fooddeliveryservice.k6 run /loadtest/smoke.js
```

Or, from `loadtest/scripts/`, which fills in the run id and the summary export:

```bash
./run.sh
```

```powershell
./run.ps1
```

`docker-compose up -d` is unchanged for everyone who is not load testing — the generator sits behind
the `loadtest` compose profile and only starts when asked for by name.

Useful variations:

| Command | What it does |
|---|---|
| `./run.sh --local` | Uses the `k6` binary on PATH against the published ports (`:3000`/`:18080`) |
| `./run.sh --env kind` | Targets the Feature 2.5 KinD cluster (Gateway `:8000`) |
| `./run.sh --run-id nightly-01` | Names the run — every correlation id carries it |
| `./run.sh -- --vus 20 --duration 2m` | Everything after `--` goes straight to k6 |
| `GATEWAY_URL=http://fooddeliveryservice.restaurants.api:8080 ./run.sh` | Bypasses the Gateway, to price the YARP hop |

## What the smoke test is for

Five VUs for thirty seconds over the read path — `GET /restaurants`, `GET /restaurants/{id}`,
`GET /restaurants/{id}/menu` — through the Gateway, with thresholds armed.

It is **not** a capacity measurement. It is the check every later milestone runs first, to prove that
auth, tagging, correlation and the pass/fail gate still work before spending twenty minutes on a
ramp. It exits non-zero when a threshold is breached, so it can be a gate rather than a report.

It also runs green against an **empty** database: the list returns `[]` and the iteration ends after
one request. That is deliberate — the harness has to be reviewable before Milestone B's seeder exists.

What it measures today, against a warm compose stack with the generator co-located (5 VUs, 30 s,
empty catalogue):

| | p95 | p99 | notes |
|---|---|---|---|
| journey (`{scope:journey}`) | 33 ms | 81 ms | list → detail → menu, through the Gateway |
| login (`{scope:auth}`) | 643 ms | — | 5 concurrent logins; PBKDF2, and it shows |
| errors | 0% | | 234/234 checks passed |

### Why there is a warm-up

`setup()` runs one full journey before any VU starts, tagged `scope: setup`.

Measured on this stack: the **first** authenticated request against a freshly started service takes
~2.5 s inside Restaurants (~3.0 s at the Gateway), against 15–40 ms in steady state. The cost is the
cold path behind `CustomClaimsTransformation` → `IPermissionService` — the MassTransit RPC to Users
and its RabbitMQ topology, plus an empty Redis permission cache. The first token request against a
cold Duende took 7.9 s. With five VUs all issuing their first request at once, five 3-second samples
out of ~70 put journey p95 at 3 s and failed a run in which every other request was fast.

Those numbers are real and worth knowing — the permission-resolution path is Milestone F #4 on the
bottleneck shortlist — so they are **measured, not hidden**: the warm-up appears in the summary under
`{scope:setup}`. It is simply not what the journey SLO is about. Process cold start is a deployment
property; the thresholds are about what a user experiences against a running system.

## Layout

```
loadtest/
├── config/
│   ├── environments.js   compose | compose-host | kind → gateway + identity URLs, credentials, run id
│   └── thresholds.js     the shared SLO block
├── lib/
│   ├── auth.js           ROPC token acquisition, cached per VU
│   ├── http.js           tagged request wrappers, correlation id, checks
│   └── fixtures.js       reads fixtures/seed.json (Milestone B writes it)
├── scenarios/            Milestone C
├── scripts/run.{sh,ps1}  the runners
├── smoke.js
└── results/              run artifacts, gitignored except results/published/
```

## Environments

| `-e ENV=` | Gateway | Identity | When |
|---|---|---|---|
| `compose` *(default)* | `http://fooddeliveryservice.gateway:8080` | `http://fooddeliveryservice.identity:8080` | k6 **inside** the compose network — the mode every published number should come from |
| `compose-host` | `http://localhost:3000` | `http://localhost:18080` | k6 on the host, while writing a script |
| `kind` | `http://localhost:8000` | `http://localhost:18080` | the Feature 2.5 cluster |

`compose` is the default because it removes Docker's host port-forwarding from the measurement, and
because it is the only mode in which the service DNS names the rest of the stack uses resolve.

## Four things the harness enforces, and why

**One login per VU, not one per iteration.** ASP.NET Identity hashes passwords with PBKDF2 and
deliberately burns CPU doing it. A script that logs in every iteration turns the exercise into a
password-hashing benchmark of one service: Identity pins a core, everything queues behind it, and
every run "finds" the same bottleneck for a reason that has nothing to do with the platform.
`lib/auth.js` caches the token in VU-local state and refreshes only on expiry. Token requests carry
their own tag (`POST /connect/token`) and their own threshold, so login cost is a visible line rather
than something smeared into the journey percentiles.

**Bounded tag cardinality.** `lib/http.js` refuses a request without an explicit `name`. Without one,
k6 tags by full URL and every restaurant id becomes its own time series — the same rule `CLAUDE.md`
states for the server side, and it bites harder here because Milestone E ships these series into the
platform's own Prometheus. Use the route template: `GET /restaurants/:id`.

**A correlation id per iteration.** Every request carries
`X-Correlation-Id: loadtest-{runId}-{vu}-{iteration}`. The Gateway preserves an inbound value
(Telemetry D) and Telemetry G carries it across the outbox/inbox boundary onto the `correlation_id`
column, so this Seq query pulls the full asynchronous fan-out of one synthetic request — including
the legs that happen seconds later in another service:

```
CorrelationId like 'loadtest-<run-id>-%'
```

During the Milestone F bottleneck hunt that is worth more than any dashboard.

**Checks that fail loudly.** Status *and* a body-shape check on at least one field, plus a guard
against a `2xx` carrying a `ProblemDetails` body. A load test that counts an application failure as a
success reports beautiful numbers for a broken system.

## Thresholds

`config/thresholds.js`, applied by every script:

| Metric | Gate |
|---|---|
| `http_req_failed` | `rate<0.01` |
| `http_req_duration{scope:journey}` | `p(95)<500`, `p(99)<1500` |
| `http_req_duration{scope:auth}` | `p(95)<2000` |
| `checks` | `rate>0.99` |

These are a starting SLO — chosen, not measured. Milestone D's baseline run is what turns them into
numbers with evidence behind them, and adds per-profile overrides (`ramp` uses `abortOnFail`, so a run
that has clearly fallen over stops at the knee instead of spending ten minutes recording zeros).

## Read this before quoting a number

**The generator shares the host with the system under test.** k6, eight .NET services, Postgres,
Redis, RabbitMQ, Prometheus, Grafana, Jaeger and Seq all run on one machine. Above roughly half the
host's cores, the results describe that contest and not the platform. Every published number has to
carry the environment it came from — host CPU/RAM, replica count, compose or KinD, generator
co-located or not. Milestone H's `docs/load-testing.md` is where that record lives.

## Credentials

Scripts default to the compose admin seed (`admin@fooddeliveryservice.com` / `admin`), because it is
the only account guaranteed to exist against a database nobody has seeded. Override with
`LOADTEST_USERNAME` / `LOADTEST_PASSWORD` — the KinD cluster applies ASP.NET Identity's real password
rules and its admin password differs. From Milestone B onwards, scenarios take their users from
`fixtures/seed.json` instead.

The prefix matters: k6 folds the whole system environment into `__ENV`, and `USERNAME` is set on
every Windows machine. A bare `__ENV.USERNAME` logs in as whoever is at the keyboard, every login
fails, and the summary reports a 100% error rate that looks like a platform fault. Prefix anything
whose bare name a shell might already own.

## Not yet built

| | |
|---|---|
| `fixtures/seed.json` | Milestone B — `lib/fixtures.js` already degrades gracefully without it |
| `scenarios/` | Milestone C — browse, order, track, driver, mixed |
| profiles, the runbook | Milestone D |
| Prometheus remote write, the `fds-load` Grafana dashboard, `handleSummary()` | Milestone E (which also replaces the deprecated `--summary-export` the runners use today) |
