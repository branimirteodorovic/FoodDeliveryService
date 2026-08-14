// Per-journey custom metrics.
//
// `http_req_duration` answers "how fast is the platform"; it cannot answer "how fast is *placing an
// order*", because in a mixed run that percentile is dominated by whichever journey is cheapest and
// most frequent — browse, by design, at 70% of the traffic. A threshold on the global metric
// therefore stays green while the write path degrades, which is the failure this feature exists to
// catch.
//
// So each journey states its own number, and Milestone D can put a threshold on it.
//
// They live in one module because k6 requires metrics to be constructed in **init context**, and
// both the standalone scenario scripts and `mixed.js` need to record into the *same* metric objects.
// A metric declared twice under one name is a runtime error.
//
// Names are lower_snake_case with no `k6_` prefix: k6's Prometheus remote-write output (Milestone E)
// prefixes them itself, so `order_placement_duration` arrives as `k6_order_placement_duration`.

import { Counter, Rate, Trend } from 'k6/metrics';

// ── Guardrail (Milestone G) ───────────────────────────────────────────────────────────────────

/**
 * Share of requests the Gateway shed with a `429`.
 *
 * **This is the metric the whole milestone exists to make visible.** Before the limiter, a platform
 * past its knee expressed overload as timeouts, which `http_req_failed` records as the platform being
 * broken — indistinguishable from a genuine 5xx, and impossible to plan capacity against. After it,
 * overload has a name and a number: *this fraction of traffic was deliberately refused, and the rest
 * was served inside the SLO*. So a 429 is deliberately **not** a `http_req_failed` and **not** a
 * failed check (see `lib/http.js`); it is counted here instead, and thresholded per profile and per
 * phase in `config/thresholds.js` / `config/profiles.js`.
 *
 * A rate above zero on `baseline` or `smoke.js` means the guardrail is mis-sized and is throttling
 * ordinary traffic — which is why those profiles gate it at essentially zero rather than ignoring it.
 *
 * k6 `Rate` carries `passes` as well as `rate`, so the summary can print both the fraction and the
 * absolute count without a second metric.
 */
export const requestsThrottled = new Rate('requests_throttled');

// ── Browse ────────────────────────────────────────────────────────────────────────────────────

/** End-to-end wall time of list → detail → menu, think time excluded. */
export const browseDuration = new Trend('browse_duration', true);

/** Browse iterations that found nothing to look at — an empty catalogue, or a page past the end. */
export const browseEmpty = new Rate('browse_empty');

// ── Order ─────────────────────────────────────────────────────────────────────────────────────

/**
 * `POST /orders` alone — the server-side write path, without the browse that preceded it. This is
 * the number Milestone F's before/after is measured on.
 */
export const orderPlacementDuration = new Trend('order_placement_duration', true);

/** Share of `POST /orders` calls that did not return an order id. */
export const orderPlacementFailures = new Rate('order_placement_failures');

/** Orders actually created. Cross-check: this must match the platform's own `orders_placed_total`. */
export const ordersPlaced = new Counter('orders_placed');

/**
 * Share of browse iterations inside `order.js` that reached a placement — the funnel the journey
 * name promises. Below 1 means browse is finding restaurants the fixture cannot order from.
 */
export const browseToOrderConversion = new Rate('browse_to_order_conversion');

/**
 * The deliberate ~1% duplicate-`Idempotency-Key` sub-case: `true` when the replay correctly returned
 * the *same* order id it did the first time. A drop here means the dedupe path is broken, and it is
 * the only way this run would ever notice.
 */
export const orderIdempotencyReplayCorrect = new Rate('order_idempotency_replay_correct');

/** `POST /orders` served from the idempotency lookup rather than a fresh insert. */
export const orderIdempotencyReplays = new Counter('order_idempotency_replays');

// ── Track ─────────────────────────────────────────────────────────────────────────────────────

/** Tracking polls issued — the read amplification per order, in one number. */
export const trackPolls = new Counter('track_polls');

/**
 * Share of delivery polls that found a delivery at all. A `404` until the restaurant marks the order
 * ready is correct; a *persistently* low rate means the kitchen side is not running and the run is
 * measuring a third of the platform (see `scenarios/restaurant.js`).
 */
export const trackDeliveryVisible = new Rate('track_delivery_visible');

/** Tracking sessions that ran to a terminal order status instead of exhausting their poll budget. */
export const trackReachedTerminal = new Rate('track_reached_terminal');

// ── Restaurant (kitchen) ──────────────────────────────────────────────────────────────────────

/** Lifecycle transitions the kitchen drove: accept, preparing, ready. */
export const kitchenTransitions = new Counter('kitchen_transitions');

/** Share of attempted transitions the platform accepted. */
export const kitchenTransitionSuccess = new Rate('kitchen_transition_success');

/** Orders sitting in `Pending` on the manager's dashboard when it was polled — the kitchen backlog. */
export const kitchenPendingBacklog = new Trend('kitchen_pending_backlog');

// ── Driver ────────────────────────────────────────────────────────────────────────────────────

/** Position reports — the platform's highest-frequency endpoint. */
export const driverLocationReports = new Counter('driver_location_reports');

/**
 * Share of offer claims that succeeded. Expected to be low and that is not a fault: with no
 * per-driver offer endpoint, a driver cannot tell whether an offer on the board is theirs without
 * trying it. See `scenarios/driver.js`. Roughly `1 / (pool size)` is the healthy value; a *zero*
 * means the drivers on the board are not the ones this run is driving.
 */
export const driverClaimHitRate = new Rate('driver_claim_hit_rate');

/** Deliveries this run's drivers accepted. */
export const driverClaimsWon = new Counter('driver_claims_won');

/** Wasted claims — the cost of the missing endpoint, measured rather than assumed. */
export const driverClaimsMissed = new Counter('driver_claims_missed');

/** Deliveries carried all the way to `Delivered`, which is what closes the order lifecycle. */
export const deliveriesCompleted = new Counter('deliveries_completed');

/** Accept → delivered wall time for one delivery, as this run's drivers performed it. */
export const deliveryFulfilmentDuration = new Trend('delivery_fulfilment_duration', true);
