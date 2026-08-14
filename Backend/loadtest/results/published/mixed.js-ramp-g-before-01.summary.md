# scenarios/mixed.js · `ramp` · run `g-before-01`

> Where is the knee? — the number this whole feature is built to produce.

| | |
|---|---|
| Shape | 1× → 2× → 4× → 6× → 8× → 10× → 13× → 16× of 2/s, 90s per step |
| Wall clock | 13:59 |
| Environment | `compose` · http://fooddeliveryservice.gateway:8080 |
| Verdict | **6 threshold(s) breached** |

> Host CPU/RAM, replica count and whether the generator was co-located are **not** captured here — k6 cannot see them. Record them next to any number quoted from this file (`loadtest/README.md` → *Before every run*).

## Traffic

| | |
|---|---|
| requests | 77,881  (92.8/s) |
| http_req_failed | 1.06% |
| shed (429) | 0.00%  (0 requests) |
| checks | 99.65%  (238,894 passed, 828 failed) |
| iterations | 20,357  (dropped 642) |

## Latency

| | |
|---|---|
| journey  {scope:journey} | p95 438.47 ms   p99 1.09 s   med 70.63 ms   max 23.37 s   n=70,366 |
| login    {scope:auth} | p95 —   n=—   — PBKDF2, and mostly the run's own ignition burst |
| POST /orders | p95 1.06 s |

## Journeys

| | |
|---|---|
| orders placed | 1,846 |
| placement failures | 4.02% |
| idempotency replays | 16 |
| tracking polls | 9,381 |
| kitchen transitions | 4,453 |
| offers claimed | 229 |
| deliveries completed | 229 |

## Phases

| Phase | Window | Rate | journey p95 | Errors | Shed (429) | Samples | login p95 |
|---|---|---|---|---|---|---|---|
| `warm` | 00:00–01:00 | 2/s | 195.07 ms | 0.22% | 0.00% | 2,067 | 5.54 s |
| `s01` | 01:00–02:30 | 2/s | 30.73 ms | 0.02% | 0.00% | 3,494 | 144.48 ms |
| `s02` | 02:45–04:15 | 4/s | 38.18 ms | 0.02% | 0.00% | 4,239 | 271.88 ms |
| `s03` | 04:30–06:00 | 8/s | 91.36 ms | 0.00% | 0.00% | 5,772 | 697.03 ms |
| `s04` | 06:15–07:45 | 12/s | 123.51 ms | 0.05% | 0.00% | 7,722 | 757.95 ms |
| `s05` | 08:00–09:30 | 16/s | 321.88 ms | 0.30% | 0.00% | 9,582 | 1.14 s |
| `s06` | 09:45–11:15 | 20/s | 477.72 ms | 0.21% | 0.00% | 11,441 | 1.82 s |
| `s07` | 11:30–13:00 | 26/s | 538.71 ms | 0.26% | 0.00% | 14,099 | 2.48 s |
| `s08` | 13:15–14:45 | 32/s | 14.39 s | 32.38% | 0.00% | 1,968 | 2.30 s |

_A phase with no samples has `p(95)=0` and passes its threshold trivially — that is what the steps after an aborted ramp look like._

## Thresholds

| | Metric | Gate | Measured |
|---|---|---|---|
| **✗** | `http_req_duration{scope:journey,phase:s07}` | `p(95)<500` | p(95)=538.71 ms |
| **✗** | `order_placement_duration` | `p(95)<1000` | p(95)=1.06 s |
| **✗** | `http_req_duration{scope:journey,phase:s08}` | `p(95)<500` | p(95)=14.39 s |
| **✗** | `http_req_failed` | `rate<0.01` | rate=0.0106 |
| **✗** | `order_placement_failures` | `rate<0.01` | rate=0.0402 |
| **✗** | `http_req_failed{phase:s08}` | `rate<0.01` | rate=0.3238 |
| ✓ | `requests_throttled{phase:s05}` | `rate<0.5` | rate=0.0000 |
| ✓ | `http_req_duration{scope:journey,phase:s05}` | `p(95)<500` | p(95)=321.88 ms |
| ✓ | `http_req_duration{scope:journey}` | `p(95)<500` | p(95)=438.47 ms |
| ✓ | `http_req_duration{scope:journey}` | `p(99)<1500` | p(99)=1.09 s |
| ✓ | `requests_throttled{phase:s03}` | `rate<0.5` | rate=0.0000 |
| ✓ | `http_req_duration{scope:auth,phase:warm}` | `p(95)<20000` | p(95)=5.54 s |
| ✓ | `http_req_duration{scope:auth,phase:s02}` | `p(95)<8000` | p(95)=271.88 ms |
| ✓ | `http_req_duration{scope:auth,phase:s03}` | `p(95)<8000` | p(95)=697.03 ms |
| ✓ | `http_req_duration{scope:journey,phase:s06}` | `p(95)<500` | p(95)=477.72 ms |
| ✓ | `http_req_duration{scope:auth,phase:s05}` | `p(95)<8000` | p(95)=1.14 s |
| ✓ | `requests_throttled{phase:s06}` | `rate<0.5` | rate=0.0000 |
| ✓ | `requests_throttled{phase:s01}` | `rate<0.5` | rate=0.0000 |
| ✓ | `requests_throttled{phase:warm}` | `rate<0.1` | rate=0.0000 |
| ✓ | `http_req_failed{phase:s02}` | `rate<0.01` | rate=0.0002 |
| ✓ | `http_req_failed{phase:s03}` | `rate<0.01` | rate=0.0000 |
| ✓ | `order_idempotency_replay_correct` | `rate>0.99` | rate=1.0000 |
| ✓ | `http_req_duration{scope:journey,phase:s01}` | `p(95)<500` | p(95)=30.73 ms |
| ✓ | `requests_throttled{phase:s02}` | `rate<0.5` | rate=0.0000 |
| ✓ | `http_req_duration{scope:journey,phase:warm}` | `p(95)<5000` | p(95)=195.07 ms |
| ✓ | `http_req_duration{scope:journey,phase:s03}` | `p(95)<500` | p(95)=91.36 ms |
| ✓ | `checks` | `rate>0.99` | rate=0.9965 |
| ✓ | `http_req_failed{phase:s07}` | `rate<0.01` | rate=0.0026 |
| ✓ | `http_req_duration{scope:journey,phase:s04}` | `p(95)<500` | p(95)=123.51 ms |
| ✓ | `http_req_duration{scope:auth,phase:s07}` | `p(95)<8000` | p(95)=2.48 s |
| ✓ | `http_req_duration{scope:auth,phase:s01}` | `p(95)<8000` | p(95)=144.48 ms |
| ✓ | `requests_throttled{phase:s08}` | `rate<0.5` | rate=0.0000 |
| ✓ | `http_req_failed{phase:warm}` | `rate<0.05` | rate=0.0022 |
| ✓ | `driver_claim_hit_rate` | `rate>0` | rate=0.0332 |
| ✓ | `http_req_duration{scope:auth,phase:s08}` | `p(95)<8000` | p(95)=2.30 s |
| ✓ | `http_req_duration{scope:auth,phase:s04}` | `p(95)<8000` | p(95)=757.95 ms |
| ✓ | `http_req_failed{phase:s01}` | `rate<0.01` | rate=0.0002 |
| ✓ | `http_req_duration{scope:journey,phase:s02}` | `p(95)<500` | p(95)=38.18 ms |
| ✓ | `kitchen_transition_success` | `rate>0.95` | rate=0.9582 |
| ✓ | `requests_throttled{phase:s07}` | `rate<0.5` | rate=0.0000 |
| ✓ | `requests_throttled` | `rate<0.5` | rate=0.0000 |
| ✓ | `http_req_failed{phase:s05}` | `rate<0.01` | rate=0.0030 |
| ✓ | `requests_throttled{phase:s04}` | `rate<0.5` | rate=0.0000 |
| ✓ | `http_req_failed{phase:s06}` | `rate<0.01` | rate=0.0021 |
| ✓ | `http_req_failed{phase:s04}` | `rate<0.01` | rate=0.0005 |
| ✓ | `http_req_duration{scope:auth,phase:s06}` | `p(95)<8000` | p(95)=1.82 s |

