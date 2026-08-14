# scenarios/mixed.js · `ramp` · run `g-after-01`

> Where is the knee? — the number this whole feature is built to produce.

| | |
|---|---|
| Shape | 1× → 2× → 4× → 6× → 8× → 10× → 13× → 16× of 2/s, 90s per step |
| Wall clock | 15:23 |
| Environment | `compose` · http://fooddeliveryservice.gateway:8080 |
| Verdict | **3 threshold(s) breached** |

> Host CPU/RAM, replica count and whether the generator was co-located are **not** captured here — k6 cannot see them. Record them next to any number quoted from this file (`loadtest/README.md` → *Before every run*).

## Traffic

| | |
|---|---|
| requests | 95,908  (104.0/s) |
| http_req_failed | 0.20% |
| shed (429) | 1.66%  (1,581 requests) |
| checks | 99.94%  (297,986 passed, 188 failed) |
| iterations | 23,490  (dropped 393) |

## Latency

| | |
|---|---|
| journey  {scope:journey} | p95 476.07 ms   p99 749.47 ms   med 115.92 ms   max 5.76 s   n=87,851 |
| login    {scope:auth} | p95 —   n=—   — PBKDF2, and mostly the run's own ignition burst |
| POST /orders | p95 782.07 ms |

## Journeys

| | |
|---|---|
| orders placed | 2,428 |
| placement failures | 0.00% |
| idempotency replays | 27 |
| tracking polls | 12,610 |
| kitchen transitions | 6,878 |
| offers claimed | 207 |
| deliveries completed | 207 |

## Phases

| Phase | Window | Rate | journey p95 | Errors | Shed (429) | Samples | login p95 |
|---|---|---|---|---|---|---|---|
| `warm` | 00:00–01:00 | 2/s | 173.58 ms | 0.00% | 0.00% | 2,313 | 5.25 s |
| `s01` | 01:00–02:30 | 2/s | 58.81 ms | 0.00% | 0.00% | 3,488 | 516.02 ms |
| `s02` | 02:45–04:15 | 4/s | 41.30 ms | 0.00% | 0.00% | 4,256 | 424.35 ms |
| `s03` | 04:30–06:00 | 8/s | 142.96 ms | 0.02% | 0.00% | 5,753 | 663.04 ms |
| `s04` | 06:15–07:45 | 12/s | 341.50 ms | 0.35% | 0.99% | 7,495 | 1.93 s |
| `s05` | 08:00–09:30 | 16/s | 306.22 ms | 0.00% | 0.00% | 9,703 | 1.34 s |
| `s06` | 09:45–11:15 | 20/s | 546.71 ms | 0.02% | 0.41% | 11,375 | 2.13 s |
| `s07` | 11:30–13:00 | 26/s | 566.10 ms | 0.39% | 3.27% | 14,292 | 2.68 s |
| `s08` | 13:15–14:45 | 32/s | 554.42 ms | 0.35% | 4.99% | 17,060 | 2.29 s |

_A phase with no samples has `p(95)=0` and passes its threshold trivially — that is what the steps after an aborted ramp look like._

## Thresholds

| | Metric | Gate | Measured |
|---|---|---|---|
| **✗** | `http_req_duration{scope:journey,phase:s07}` | `p(95)<500` | p(95)=566.10 ms |
| **✗** | `http_req_duration{scope:journey,phase:s08}` | `p(95)<500` | p(95)=554.42 ms |
| **✗** | `http_req_duration{scope:journey,phase:s06}` | `p(95)<500` | p(95)=546.71 ms |
| ✓ | `requests_throttled{phase:warm}` | `rate<0.1` | rate=0.0000 |
| ✓ | `http_req_duration{scope:auth,phase:s08}` | `p(95)<8000` | p(95)=2.29 s |
| ✓ | `http_req_duration{scope:journey,phase:s04}` | `p(95)<500` | p(95)=341.50 ms |
| ✓ | `order_idempotency_replay_correct` | `rate>0.99` | rate=1.0000 |
| ✓ | `http_req_duration{scope:auth,phase:s05}` | `p(95)<8000` | p(95)=1.34 s |
| ✓ | `requests_throttled{phase:s03}` | `rate<0.5` | rate=0.0000 |
| ✓ | `http_req_failed{phase:s01}` | `rate<0.01` | rate=0.0000 |
| ✓ | `requests_throttled{phase:s04}` | `rate<0.5` | rate=0.0099 |
| ✓ | `http_req_failed{phase:s07}` | `rate<0.01` | rate=0.0039 |
| ✓ | `http_req_duration{scope:journey,phase:s05}` | `p(95)<500` | p(95)=306.22 ms |
| ✓ | `http_req_duration{scope:journey}` | `p(95)<500` | p(95)=476.07 ms |
| ✓ | `http_req_duration{scope:journey}` | `p(99)<1500` | p(99)=749.47 ms |
| ✓ | `requests_throttled{phase:s08}` | `rate<0.5` | rate=0.0499 |
| ✓ | `checks` | `rate>0.99` | rate=0.9994 |
| ✓ | `http_req_duration{scope:auth,phase:s07}` | `p(95)<8000` | p(95)=2.68 s |
| ✓ | `http_req_failed{phase:s03}` | `rate<0.01` | rate=0.0002 |
| ✓ | `kitchen_transition_success` | `rate>0.95` | rate=0.9913 |
| ✓ | `http_req_duration{scope:auth,phase:s03}` | `p(95)<8000` | p(95)=663.04 ms |
| ✓ | `requests_throttled{phase:s05}` | `rate<0.5` | rate=0.0000 |
| ✓ | `http_req_duration{scope:auth,phase:warm}` | `p(95)<20000` | p(95)=5.25 s |
| ✓ | `requests_throttled{phase:s01}` | `rate<0.5` | rate=0.0000 |
| ✓ | `http_req_duration{scope:auth,phase:s01}` | `p(95)<8000` | p(95)=516.02 ms |
| ✓ | `order_placement_failures` | `rate<0.01` | rate=0.0000 |
| ✓ | `requests_throttled{phase:s06}` | `rate<0.5` | rate=0.0041 |
| ✓ | `http_req_duration{scope:journey,phase:warm}` | `p(95)<5000` | p(95)=173.58 ms |
| ✓ | `http_req_duration{scope:auth,phase:s04}` | `p(95)<8000` | p(95)=1.93 s |
| ✓ | `requests_throttled{phase:s07}` | `rate<0.5` | rate=0.0327 |
| ✓ | `http_req_duration{scope:journey,phase:s03}` | `p(95)<500` | p(95)=142.96 ms |
| ✓ | `http_req_duration{scope:auth,phase:s02}` | `p(95)<8000` | p(95)=424.35 ms |
| ✓ | `http_req_failed{phase:s08}` | `rate<0.01` | rate=0.0035 |
| ✓ | `http_req_duration{scope:journey,phase:s02}` | `p(95)<500` | p(95)=41.30 ms |
| ✓ | `driver_claim_hit_rate` | `rate>0` | rate=0.0281 |
| ✓ | `http_req_failed{phase:s06}` | `rate<0.01` | rate=0.0002 |
| ✓ | `http_req_failed` | `rate<0.01` | rate=0.0020 |
| ✓ | `order_placement_duration` | `p(95)<1000` | p(95)=782.07 ms |
| ✓ | `http_req_failed{phase:warm}` | `rate<0.05` | rate=0.0000 |
| ✓ | `requests_throttled` | `rate<0.5` | rate=0.0166 |
| ✓ | `http_req_failed{phase:s02}` | `rate<0.01` | rate=0.0000 |
| ✓ | `requests_throttled{phase:s02}` | `rate<0.5` | rate=0.0000 |
| ✓ | `http_req_duration{scope:auth,phase:s06}` | `p(95)<8000` | p(95)=2.13 s |
| ✓ | `http_req_failed{phase:s05}` | `rate<0.01` | rate=0.0000 |
| ✓ | `http_req_duration{scope:journey,phase:s01}` | `p(95)<500` | p(95)=58.81 ms |
| ✓ | `http_req_failed{phase:s04}` | `rate<0.01` | rate=0.0035 |

