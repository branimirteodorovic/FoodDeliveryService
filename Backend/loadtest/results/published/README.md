# Published run artifacts

The summaries the repo's documentation quotes. Everything else under `results/` is gitignored — these
are here because a published number with no artifact behind it is an assertion, not evidence, and
because Prometheus keeps seven days on a volume that is explicitly disposable (Milestone E).

Each file here is k6's `--summary-export` JSON for one run of `scenarios/mixed.js`.

**Runs from Milestone E onwards have a different shape and one more file.** `handleSummary()`
(`config/output.js`) replaced the deprecated `--summary-export`, so a new `.summary.json` is k6's full
summary object — the statistics moved one level down, into `metrics[name].values` — and it arrives
with a `.summary.md` beside it holding the same run as a table, plus a `.platform.json` of the
server-side Prometheus series when the run was started with `--prometheus`. The five files below
predate that and keep the flat shape; they are not being regenerated, because a published artifact
that gets rewritten is not evidence of anything.

| File | Profile | What it is |
|---|---|---|
| `mixed.js-baseline-baseline-01.summary.json` | `baseline` | The reference run. Every other number in `../../README.md` is read against it. |
| `mixed.js-baseline-baseline-02.summary.json` | `baseline` | The same profile, immediately afterwards — the repeatability check. Journey p95 agrees with the first to 1.8%. |
| `mixed.js-spike-spike-03.summary.json` | `spike` | 10× for 60 s. `peak` p95 313 ms, `post` back to 55.78 ms — the platform recovered inside the 3-minute window. |
| `mixed.js-ramp-ramp-01.summary.json` | `ramp` | 2 → 20 customers/s in eight steps. Every step green: the knee is above the profile's default range. |
| `mixed.js-ramp-ramp-02.summary.json` | `ramp` | The continuation, `RAMP_STEPS=10,13,16,20,25`. **The knee**: 20 customers/s green at p95 230 ms, 26 customers/s gone at 1.85 s and 3.8% errors, and the run aborted there. The steps it never reached are in the file with `p(95)=0` and no samples — the trivially-passing empty phase, worth seeing once. |
| `mixed.js-ramp-f-before-02.summary.{json,md}` | `ramp` | **Milestone F, the before.** Same profile as `ramp-02`, one variable at a time from here on. 678 `53300` connection refusals underneath it; journey p95 746 ms, 0.40% errors, 2.09% of placements failed. |
| `mixed.js-ramp-f-pipeline-01.summary.{json,md}` | `ramp` | **Milestone F, after the event-pipeline change** (dispatch index + 1 s/50 + `SKIP LOCKED`). Backlog drain 2.96 → 9.44 rows/s, errors to zero, journey p95 slightly *worse* at 789 ms — because the run stopped failing and completed more work. |
| `mixed.js-ramp-f-pools-01.summary.{json,md}` | `ramp` | **Milestone F, after bounding the Npgsql pools.** journey p95 586 ms, p99 2.31 s → 1.15 s, `POST /orders` p95 1.43 s → 919 ms, backends 88/100 → 87/200. The best of the three. |
| `mixed.js-ramp-g-before-01.summary.{json,md}` | `ramp` | **Milestone G, the before** — the stock 8-step ramp (2 → 32 customers/s) with `RateLimiting__Enabled=false`. **The cliff**: green to 26 customers/s (p95 539 ms), then s08 collapses to p95 **14.39 s**, **32.4%** errors and 1,968 served requests where the previous step served 14,099. |
| `mixed.js-ramp-g-after-01.summary.{json,md}` | `ramp` | **Milestone G, the after** — identical profile, limiter on. **The plateau**: s08 holds p95 **554 ms** at 0.35% errors and **17,060** served requests, shedding 4.99%. Run-wide throughput +12%, p99 1.09 s → 749 ms, placement failures 4.02% → 0.00%. |

The three `f-*` runs are one controlled sequence and are only meaningful read together, in that order
— `docs/load-testing.md` is the log that explains what changed between each and why one of the three
predicted fixes was reverted instead of shipped. They carry the post-Milestone-E shape described
above, including the `.summary.md` beside each.

The two `g-*` runs are a pair and mean nothing apart: same profile, same machine, same afternoon,
**one variable** — the Gateway's `RateLimiting__Enabled`. They also use the *stock* eight-step ramp
rather than the `f-*` runs' `RAMP_STEPS=10,13,16,20,25`, because the question changed: the `f-*` runs
were bisecting a known knee, while these two have to show what happens on either side of it.

## The environment they came from

All of them, same machine, nothing else running on it:

```
compose · 8 vCPU · 7.6 GB to Docker · generator co-located · 1 replica per service
up:   gateway identity users orders restaurants delivery notifications
      postgres redis rabbitmq seq jaeger otel-collector
down: realtime frauddetection prometheus grafana blackbox
fixture: 20 restaurants × 24 menu items · 500 customers · 50 drivers
k6:   grafana/k6:latest, inside the compose network (ENV=compose)
```

**These numbers do not transfer to another machine**, and that is the point of writing the
environment down next to them rather than in a commit message. The generator shares eight cores with
everything it is measuring; above roughly half of them the results describe that contest. See
*Read this before quoting a number* in `../../README.md`.

## The graphs are drawn from these files

`node scripts/plot.mjs` (from `loadtest/`) reads the six post-Milestone-E summaries here and writes
the three SVGs in `docs/assets/loadtest/` that the project README and `docs/load-testing.md` embed.
Nothing else is needed — no stack, no Grafana, no network — which is the point: Prometheus keeps seven
days, these files keep forever, so every published picture stays redrawable from published evidence.

If a file here is ever changed, the graphs change with it on the next run of that script. That is the
intended tripwire. It is also why the five pre-Milestone-E files are not regenerated into the new
shape: the script does not read them, and rewriting a published artifact to suit a tool is how
evidence stops being evidence.

## Reading one without k6

The interesting keys are `metrics["http_req_duration{scope:journey}"]` (the journey SLO),
`metrics["http_req_duration{scope:journey,phase:sNN}"]` (per step, staged profiles only) and
`metrics.http_req_failed`. `checks` carries the body-shape assertions — a run with a healthy latency
profile and a sagging check rate is a platform returning fast wrong answers.

In these five files the statistics sit directly on the metric (`metrics["…"]["p(95)"]`). In anything
written after Milestone E they sit under `.values` (`metrics["…"].values["p(95)"]`), the metric also
carries its `type` and its `thresholds`, and there is a `.summary.md` next to it that needs no key
paths at all.
