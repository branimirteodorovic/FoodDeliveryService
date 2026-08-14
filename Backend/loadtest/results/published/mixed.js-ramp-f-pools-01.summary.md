# scenarios/mixed.js · `ramp` · run `f-pools-01`

> Where is the knee? — the number this whole feature is built to produce.

| | |
|---|---|
| Shape | 10× → 13× → 16× → 20× → 25× of 2/s, 90s per step |
| Wall clock | 03:02 |
| Environment | `compose` · http://fooddeliveryservice.gateway:8080 |
| Verdict | **2 threshold(s) breached** |

> Host CPU/RAM, replica count and whether the generator was co-located are **not** captured here — k6 cannot see them. Record them next to any number quoted from this file (`loadtest/README.md` → *Before every run*).

## Traffic

| | |
|---|---|
| requests | 9,087  (49.9/s) |
| http_req_failed | 0.17% |
| checks | 99.94%  (26,034 passed, 15 failed) |
| iterations | 2,613  (dropped 149) |

## Latency

| | |
|---|---|
| journey  {scope:journey} | p95 585.52 ms   p99 1.15 s   med 153.86 ms   max 11.02 s   n=7,639 |
| login    {scope:auth} | p95 —   n=—   — PBKDF2, and mostly the run's own ignition burst |
| POST /orders | p95 919.37 ms |

## Journeys

| | |
|---|---|
| orders placed | 192 |
| placement failures | 0.00% |
| tracking polls | 610 |
| kitchen transitions | 531 |
| offers claimed | 47 |
| deliveries completed | 44 |

## Phases

| Phase | Window | Rate | journey p95 | Errors | Samples | login p95 |
|---|---|---|---|---|---|---|
| `warm` | 00:00–01:00 | 2/s | 1.04 s | 0.25% | 1,528 | 9.32 s |
| `s01` | 01:00–02:30 | 20/s | 572.12 ms | 0.14% | 5,603 | 2.82 s |
| `s02` | 02:45–04:15 | 26/s | 0.00 ms | 0.00% | 0 | 0.00 ms |
| `s03` | 04:30–06:00 | 32/s | 0.00 ms | 0.00% | 0 | 0.00 ms |
| `s04` | 06:15–07:45 | 40/s | 0.00 ms | 0.00% | 0 | 0.00 ms |
| `s05` | 08:00–09:30 | 50/s | 0.00 ms | 0.00% | 0 | 0.00 ms |

_A phase with no samples has `p(95)=0` and passes its threshold trivially — that is what the steps after an aborted ramp look like._

## Thresholds

| | Metric | Gate | Measured |
|---|---|---|---|
| **✗** | `http_req_duration{scope:journey,phase:s01}` | `p(95)<500` | p(95)=572.12 ms |
| **✗** | `http_req_duration{scope:journey}` | `p(95)<500` | p(95)=585.52 ms |
| ✓ | `kitchen_transition_success` | `rate>0.95` | rate=0.9718 |
| ✓ | `http_req_failed{phase:s02}` | `rate<0.01` | rate=0.0000 |
| ✓ | `order_idempotency_replay_correct` | `rate>0.99` | rate=0.0000 |
| ✓ | `http_req_duration{scope:journey,phase:warm}` | `p(95)<5000` | p(95)=1.04 s |
| ✓ | `http_req_failed{phase:s03}` | `rate<0.01` | rate=0.0000 |
| ✓ | `http_req_duration{scope:journey,phase:s03}` | `p(95)<500` | p(95)=0.00 ms |
| ✓ | `http_req_failed` | `rate<0.01` | rate=0.0017 |
| ✓ | `http_req_duration{scope:journey,phase:s04}` | `p(95)<500` | p(95)=0.00 ms |
| ✓ | `http_req_duration{scope:journey}` | `p(99)<1500` | p(99)=1.15 s |
| ✓ | `http_req_duration{scope:auth,phase:s04}` | `p(95)<8000` | p(95)=0.00 ms |
| ✓ | `http_req_duration{scope:journey,phase:s05}` | `p(95)<500` | p(95)=0.00 ms |
| ✓ | `order_placement_failures` | `rate<0.01` | rate=0.0000 |
| ✓ | `http_req_duration{scope:journey,phase:s02}` | `p(95)<500` | p(95)=0.00 ms |
| ✓ | `http_req_duration{scope:auth,phase:s02}` | `p(95)<8000` | p(95)=0.00 ms |
| ✓ | `http_req_failed{phase:warm}` | `rate<0.05` | rate=0.0025 |
| ✓ | `http_req_duration{scope:auth,phase:s05}` | `p(95)<8000` | p(95)=0.00 ms |
| ✓ | `http_req_duration{scope:auth,phase:s03}` | `p(95)<8000` | p(95)=0.00 ms |
| ✓ | `http_req_failed{phase:s04}` | `rate<0.01` | rate=0.0000 |
| ✓ | `http_req_failed{phase:s05}` | `rate<0.01` | rate=0.0000 |
| ✓ | `driver_claim_hit_rate` | `rate>0` | rate=0.0485 |
| ✓ | `http_req_duration{scope:auth,phase:s01}` | `p(95)<8000` | p(95)=2.82 s |
| ✓ | `http_req_duration{scope:auth,phase:warm}` | `p(95)<20000` | p(95)=9.32 s |
| ✓ | `checks` | `rate>0.99` | rate=0.9994 |
| ✓ | `order_placement_duration` | `p(95)<1000` | p(95)=919.37 ms |
| ✓ | `http_req_failed{phase:s01}` | `rate<0.01` | rate=0.0014 |

