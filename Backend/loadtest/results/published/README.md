# Published run artifacts

The summaries the repo's documentation quotes. Everything else under `results/` is gitignored — these
are here because a published number with no artifact behind it is an assertion, not evidence, and
because Prometheus keeps seven days on a volume that is explicitly disposable (Milestone E).

Each file is k6's `--summary-export` JSON for one run of `scenarios/mixed.js`.

| File | Profile | What it is |
|---|---|---|
| `mixed.js-baseline-baseline-01.summary.json` | `baseline` | The reference run. Every other number in `../../README.md` is read against it. |
| `mixed.js-baseline-baseline-02.summary.json` | `baseline` | The same profile, immediately afterwards — the repeatability check. Journey p95 agrees with the first to 1.8%. |
| `mixed.js-spike-spike-03.summary.json` | `spike` | 10× for 60 s. `peak` p95 313 ms, `post` back to 55.78 ms — the platform recovered inside the 3-minute window. |
| `mixed.js-ramp-ramp-01.summary.json` | `ramp` | 2 → 20 customers/s in eight steps. Every step green: the knee is above the profile's default range. |
| `mixed.js-ramp-ramp-02.summary.json` | `ramp` | The continuation, `RAMP_STEPS=10,13,16,20,25`. **The knee**: 20 customers/s green at p95 230 ms, 26 customers/s gone at 1.85 s and 3.8% errors, and the run aborted there. The steps it never reached are in the file with `p(95)=0` and no samples — the trivially-passing empty phase, worth seeing once. |

## The environment they came from

All five, same machine, same day, nothing else running on it:

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

## Reading one without k6

The interesting keys are `metrics["http_req_duration{scope:journey}"]` (the journey SLO),
`metrics["http_req_duration{scope:journey,phase:sNN}"]` (per step, staged profiles only) and
`metrics.http_req_failed`. `checks` carries the body-shape assertions — a run with a healthy latency
profile and a sagging check rate is a platform returning fast wrong answers.
