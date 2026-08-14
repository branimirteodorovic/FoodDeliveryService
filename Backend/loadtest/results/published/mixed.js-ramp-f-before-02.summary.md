# scenarios/mixed.js · `ramp` · run `f-before-02`

> Where is the knee? — the number this whole feature is built to produce.

| | |
|---|---|
| Shape | 10× → 13× → 16× → 20× → 25× of 2/s, 90s per step |
| Wall clock | 03:01 |
| Environment | `compose` · http://fooddeliveryservice.gateway:8080 |
| Verdict | **5 threshold(s) breached** |

> Host CPU/RAM, replica count and whether the generator was co-located are **not** captured here — k6 cannot see them. Record them next to any number quoted from this file (`loadtest/README.md` → *Before every run*).

## Traffic

| | |
|---|---|
| requests | 9,390  (51.7/s) |
| http_req_failed | 0.40% |
| checks | 99.86%  (26,884 passed, 38 failed) |
| iterations | 2,922  (dropped 233) |

## Latency

| | |
|---|---|
| journey  {scope:journey} | p95 746.03 ms   p99 2.31 s   med 81.98 ms   max 10.75 s   n=7,823 |
| login    {scope:auth} | p95 —   n=—   — PBKDF2, and mostly the run's own ignition burst |
| POST /orders | p95 1.43 s |

## Journeys

| | |
|---|---|
| orders placed | 185 |
| placement failures | 2.09% |
| idempotency replays | 2 |
| tracking polls | 622 |
| kitchen transitions | 465 |
| offers claimed | 63 |
| deliveries completed | 61 |

## Phases

| Phase | Window | Rate | journey p95 | Errors | Samples | login p95 |
|---|---|---|---|---|---|---|
| `warm` | 00:00–01:00 | 2/s | 235.90 ms | 0.00% | 1,669 | 9.37 s |
| `s01` | 01:00–02:30 | 20/s | 576.24 ms | 0.08% | 5,691 | 4.87 s |
| `s02` | 02:45–04:15 | 26/s | 0.00 ms | 0.00% | 0 | 0.00 ms |
| `s03` | 04:30–06:00 | 32/s | 0.00 ms | 0.00% | 0 | 0.00 ms |
| `s04` | 06:15–07:45 | 40/s | 0.00 ms | 0.00% | 0 | 0.00 ms |
| `s05` | 08:00–09:30 | 50/s | 0.00 ms | 0.00% | 0 | 0.00 ms |

_A phase with no samples has `p(95)=0` and passes its threshold trivially — that is what the steps after an aborted ramp look like._

## Thresholds

| | Metric | Gate | Measured |
|---|---|---|---|
| **✗** | `order_placement_duration` | `p(95)<1000` | p(95)=1.43 s |
| **✗** | `order_placement_failures` | `rate<0.01` | rate=0.0209 |
| **✗** | `http_req_duration{scope:journey}` | `p(95)<500` | p(95)=746.03 ms |
| **✗** | `http_req_duration{scope:journey}` | `p(99)<1500` | p(99)=2.31 s |
| **✗** | `http_req_duration{scope:journey,phase:s01}` | `p(95)<500` | p(95)=576.24 ms |
| ✓ | `http_req_failed` | `rate<0.01` | rate=0.0040 |
| ✓ | `kitchen_transition_success` | `rate>0.95` | rate=0.9849 |
| ✓ | `http_req_failed{phase:s02}` | `rate<0.01` | rate=0.0000 |
| ✓ | `http_req_failed{phase:s05}` | `rate<0.01` | rate=0.0000 |
| ✓ | `http_req_duration{scope:auth,phase:s02}` | `p(95)<8000` | p(95)=0.00 ms |
| ✓ | `http_req_duration{scope:auth,phase:s01}` | `p(95)<8000` | p(95)=4.87 s |
| ✓ | `http_req_duration{scope:journey,phase:s04}` | `p(95)<500` | p(95)=0.00 ms |
| ✓ | `http_req_duration{scope:auth,phase:s03}` | `p(95)<8000` | p(95)=0.00 ms |
| ✓ | `http_req_duration{scope:journey,phase:s03}` | `p(95)<500` | p(95)=0.00 ms |
| ✓ | `http_req_duration{scope:journey,phase:warm}` | `p(95)<5000` | p(95)=235.90 ms |
| ✓ | `order_idempotency_replay_correct` | `rate>0.99` | rate=1.0000 |
| ✓ | `http_req_failed{phase:s04}` | `rate<0.01` | rate=0.0000 |
| ✓ | `http_req_failed{phase:warm}` | `rate<0.05` | rate=0.0000 |
| ✓ | `checks` | `rate>0.99` | rate=0.9986 |
| ✓ | `http_req_duration{scope:auth,phase:warm}` | `p(95)<20000` | p(95)=9.37 s |
| ✓ | `http_req_failed{phase:s03}` | `rate<0.01` | rate=0.0000 |
| ✓ | `http_req_duration{scope:auth,phase:s04}` | `p(95)<8000` | p(95)=0.00 ms |
| ✓ | `http_req_failed{phase:s01}` | `rate<0.01` | rate=0.0008 |
| ✓ | `http_req_duration{scope:journey,phase:s05}` | `p(95)<500` | p(95)=0.00 ms |
| ✓ | `driver_claim_hit_rate` | `rate>0` | rate=0.0695 |
| ✓ | `http_req_duration{scope:journey,phase:s02}` | `p(95)<500` | p(95)=0.00 ms |
| ✓ | `http_req_duration{scope:auth,phase:s05}` | `p(95)<8000` | p(95)=0.00 ms |

