# scenarios/mixed.js · `ramp` · run `f-pipeline-01`

> Where is the knee? — the number this whole feature is built to produce.

| | |
|---|---|
| Shape | 10× → 13× → 16× → 20× → 25× of 2/s, 90s per step |
| Wall clock | 03:02 |
| Environment | `compose` · http://fooddeliveryservice.gateway:8080 |
| Verdict | **4 threshold(s) breached** |

> Host CPU/RAM, replica count and whether the generator was co-located are **not** captured here — k6 cannot see them. Record them next to any number quoted from this file (`loadtest/README.md` → *Before every run*).

## Traffic

| | |
|---|---|
| requests | 9,540  (52.5/s) |
| http_req_failed | 0.00% |
| checks | 100.00%  (27,403 passed, 0 failed) |
| iterations | 2,704  (dropped 218) |

## Latency

| | |
|---|---|
| journey  {scope:journey} | p95 789.36 ms   p99 2.03 s   med 208.31 ms   max 11.89 s   n=8,070 |
| login    {scope:auth} | p95 —   n=—   — PBKDF2, and mostly the run's own ignition burst |
| POST /orders | p95 2.00 s |

## Journeys

| | |
|---|---|
| orders placed | 209 |
| placement failures | 0.00% |
| idempotency replays | 3 |
| tracking polls | 664 |
| kitchen transitions | 628 |
| offers claimed | 46 |
| deliveries completed | 43 |

## Phases

| Phase | Window | Rate | journey p95 | Errors | Samples | login p95 |
|---|---|---|---|---|---|---|
| `warm` | 00:00–01:00 | 2/s | 754.42 ms | 0.00% | 1,608 | 9.19 s |
| `s01` | 01:00–02:30 | 20/s | 674.55 ms | 0.00% | 5,525 | 3.68 s |
| `s02` | 02:45–04:15 | 26/s | 0.00 ms | 0.00% | 0 | 0.00 ms |
| `s03` | 04:30–06:00 | 32/s | 0.00 ms | 0.00% | 0 | 0.00 ms |
| `s04` | 06:15–07:45 | 40/s | 0.00 ms | 0.00% | 0 | 0.00 ms |
| `s05` | 08:00–09:30 | 50/s | 0.00 ms | 0.00% | 0 | 0.00 ms |

_A phase with no samples has `p(95)=0` and passes its threshold trivially — that is what the steps after an aborted ramp look like._

## Thresholds

| | Metric | Gate | Measured |
|---|---|---|---|
| **✗** | `http_req_duration{scope:journey,phase:s01}` | `p(95)<500` | p(95)=674.55 ms |
| **✗** | `order_placement_duration` | `p(95)<1000` | p(95)=2.00 s |
| **✗** | `http_req_duration{scope:journey}` | `p(95)<500` | p(95)=789.36 ms |
| **✗** | `http_req_duration{scope:journey}` | `p(99)<1500` | p(99)=2.03 s |
| ✓ | `http_req_duration{scope:auth,phase:warm}` | `p(95)<20000` | p(95)=9.19 s |
| ✓ | `http_req_duration{scope:auth,phase:s03}` | `p(95)<8000` | p(95)=0.00 ms |
| ✓ | `http_req_duration{scope:journey,phase:s03}` | `p(95)<500` | p(95)=0.00 ms |
| ✓ | `kitchen_transition_success` | `rate>0.95` | rate=1.0000 |
| ✓ | `order_placement_failures` | `rate<0.01` | rate=0.0000 |
| ✓ | `http_req_duration{scope:journey,phase:s04}` | `p(95)<500` | p(95)=0.00 ms |
| ✓ | `http_req_failed` | `rate<0.01` | rate=0.0000 |
| ✓ | `http_req_duration{scope:auth,phase:s05}` | `p(95)<8000` | p(95)=0.00 ms |
| ✓ | `http_req_failed{phase:s02}` | `rate<0.01` | rate=0.0000 |
| ✓ | `http_req_failed{phase:s05}` | `rate<0.01` | rate=0.0000 |
| ✓ | `order_idempotency_replay_correct` | `rate>0.99` | rate=1.0000 |
| ✓ | `http_req_failed{phase:s04}` | `rate<0.01` | rate=0.0000 |
| ✓ | `http_req_duration{scope:auth,phase:s04}` | `p(95)<8000` | p(95)=0.00 ms |
| ✓ | `http_req_failed{phase:s01}` | `rate<0.01` | rate=0.0000 |
| ✓ | `http_req_duration{scope:journey,phase:s02}` | `p(95)<500` | p(95)=0.00 ms |
| ✓ | `checks` | `rate>0.99` | rate=1.0000 |
| ✓ | `http_req_duration{scope:journey,phase:warm}` | `p(95)<5000` | p(95)=754.42 ms |
| ✓ | `driver_claim_hit_rate` | `rate>0` | rate=0.0474 |
| ✓ | `http_req_duration{scope:auth,phase:s01}` | `p(95)<8000` | p(95)=3.68 s |
| ✓ | `http_req_duration{scope:auth,phase:s02}` | `p(95)<8000` | p(95)=0.00 ms |
| ✓ | `http_req_failed{phase:s03}` | `rate<0.01` | rate=0.0000 |
| ✓ | `http_req_duration{scope:journey,phase:s05}` | `p(95)<500` | p(95)=0.00 ms |
| ✓ | `http_req_failed{phase:warm}` | `rate<0.05` | rate=0.0000 |

