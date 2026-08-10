// Order — the write path.
//
//   browse (list → detail → menu)  →  POST /orders with 1–4 lines from that restaurant's menu
//
// The browse in front of it is not padding: it is where the restaurant and the item ids come from,
// so the funnel this measures ("browse → place order") is the one a customer actually walks, and
// `browse_to_order_conversion` says how often it completes.
//
// ── The one way to accidentally lie in this whole feature ────────────────────────────────────────
//
// `PlaceOrderCommandHandler` looks up `Idempotency-Key` **first** and returns the existing order id
// if it hits — before touching the customer, the restaurant replica, the menu pricing or the insert.
// A script that reuses one key therefore stops creating orders after its first iteration and starts
// measuring a single indexed `SELECT` on `idempotency_key`. Throughput goes up, latency collapses,
// the summary looks spectacular, and none of it happened. `orders_placed_total` on the platform side
// would sit flat while this script reported thousands of successful placements.
//
// So: a fresh key every iteration, and the `orders_placed` counter here exists to be compared
// against the platform's own `orders_placed_total` at the end of a run. If they disagree, this
// script is measuring HTTP responses that never became orders.
//
// The dedupe path still deserves measuring — it is real behaviour, a mobile client retrying over a
// flaky connection — so ~1% of iterations replay the previous key **on purpose**, tagged, counted,
// and checked for the property that matters: the replay must return the *same* order id.

import { sleep } from 'k6';
import exec from 'k6/execution';
import { runId } from '../config/environments.js';
import { sloThresholds } from '../config/thresholds.js';
import { customerForThisVu, tokenFor } from '../lib/actors.js';
import { deliveryAddressFor, pickSome, thinkTime } from '../lib/domain.js';
import { announce, fixture, requireFixture } from '../lib/fixtures.js';
import { gatewayUrl, post } from '../lib/http.js';
import {
    browseToOrderConversion,
    orderIdempotencyReplayCorrect,
    orderIdempotencyReplays,
    orderPlacementDuration,
    orderPlacementFailures,
    ordersPlaced,
} from '../lib/metrics.js';
import { browseJourney } from './browse.js';

/** Lines per order. Small on purpose — pricing is per line and the median basket is not large. */
const MIN_LINES = 1;
const MAX_LINES = 4;

/** Share of iterations that deliberately replay the previous iteration's key. */
const REPLAY_RATE = Number(__ENV.ORDER_REPLAY_RATE || 0.01);

/**
 * VU-local memory of the last key and the id it produced — the whole state the replay sub-case
 * needs. Per-VU by construction: k6 gives every VU its own runtime.
 */
let lastPlacement = null;

export const options = {
    vus: Number(__ENV.VUS || 5),
    duration: __ENV.DURATION || '1m',
    thresholds: {
        ...sloThresholds(),
        // The write path gets its own gate. In a mixed run the global percentile is dominated by
        // browse, so this is the only threshold that can fail on a slow `POST /orders`.
        order_placement_duration: ['p(95)<1000'],
        order_placement_failures: ['rate<0.01'],
        // A replay that returns a different order id means the dedupe is broken and the platform is
        // creating duplicate orders under retry. Nothing else in the run would notice.
        order_idempotency_replay_correct: ['rate>0.99'],
    },
    tags: { testid: `order-${runId}` },
};

export function setup() {
    requireFixture('order.js');
    announce('order.js', `replay rate ${(REPLAY_RATE * 100).toFixed(1)}%`);

    if (fixture.customers.length < (options.vus || 0)) {
        // Not fatal — two VUs sharing a customer is only unrealistic, not incorrect — but it changes
        // what the run measures (one customer's order history grows at twice the rate).
        console.warn(
            `order.js: ${options.vus} VUs over ${fixture.customers.length} fixture customers — ` +
                'VUs will share identities. Re-seed with more customers for a realistic spread.'
        );
    }

    return { startedOnUtc: new Date().toISOString() };
}

export default function () {
    orderJourney();

    sleep(thinkTime(1, 3));
}

/**
 * Browse, then order. Returns `{orderId, restaurant}` or `null` if the journey did not get that far.
 *
 * @param {string} [token] a token the caller already holds.
 */
export function orderJourney(token) {
    const bearer = token || tokenFor(customerForThisVu());
    const target = browseJourney(bearer);

    if (!target) {
        // Browsed, found nothing orderable. Recorded rather than retried: an unseeded or drifted
        // fixture should show up as a conversion collapse, not as a silently longer iteration.
        browseToOrderConversion.add(false);

        return null;
    }

    const placement = placeOrder(target, bearer);

    browseToOrderConversion.add(Boolean(placement));

    return placement;
}

/**
 * `POST /orders` for one restaurant. Exported because `track.js` needs an order to track and
 * `mixed.js` composes the journeys — both want the placement without the browse in front of it.
 *
 * @param {object} restaurant a fixture entry carrying `restaurantId`, `menuItemIds` and coordinates
 * @param {string} token
 * @returns {{orderId: string, restaurant: object, replayed: boolean}|null}
 */
export function placeOrder(restaurant, token) {
    const lines = pickSome(
        restaurant.menuItemIds,
        MIN_LINES + Math.floor(Math.random() * (MAX_LINES - MIN_LINES + 1))
    ).map((menuItemId) => ({ menuItemId, quantity: 1 + Math.floor(Math.random() * 2) }));

    if (lines.length === 0) {
        return null;
    }

    const replaying = Boolean(lastPlacement) && Math.random() < REPLAY_RATE;
    const idempotencyKey = replaying ? lastPlacement.key : freshIdempotencyKey();

    const payload = {
        restaurantId: restaurant.restaurantId,
        items: lines,
        deliveryAddress: deliveryAddressFor(restaurant),
        paymentMethod: 'CashOnDelivery',
    };

    const result = post(gatewayUrl('orders'), payload, {
        name: 'POST /orders',
        token,
        // The header the whole comment block at the top of this file is about. Not optional in
        // practice: `PlaceOrderCommandValidator` requires a non-empty key, so a placement without it
        // is a 400 — which is how the omission was caught, and it is worth knowing that the platform
        // refuses an un-keyed order rather than quietly accepting one.
        headers: { 'Idempotency-Key': idempotencyKey },
        // Tagged so the replay sub-case can be read as its own line in the summary: the dedupe path
        // is a `SELECT` and the fresh path is an insert plus an outbox row, and averaging the two
        // together hides both.
        tags: { placement: replaying ? 'replay' : 'fresh' },
        body: { 'returns an order id': (json) => typeof json === 'string' && json.length === 36 },
    });

    // The response's own timing, not wall time — this must not include the script's JSON work.
    orderPlacementDuration.add(result.response.timings.duration, { placement: replaying ? 'replay' : 'fresh' });
    orderPlacementFailures.add(!result.ok);

    if (!result.ok) {
        return null;
    }

    const orderId = result.json;

    if (replaying) {
        orderIdempotencyReplays.add(1);

        // The property the sub-case exists for. A *different* id means the unique index did not hold
        // and the platform created a second order for a retried request.
        orderIdempotencyReplayCorrect.add(orderId === lastPlacement.orderId);
    } else {
        ordersPlaced.add(1);
        lastPlacement = { key: idempotencyKey, orderId };
    }

    return { orderId, restaurant, replayed: replaying };
}

/**
 * A key that is unique per placement and *legible* — `PlaceOrderCommandValidator` allows 100
 * characters, and spending some of them on the run, scenario, VU and iteration means an order found
 * in the database months later can be traced back to the run that made it. A random suffix keeps two
 * runs that reuse a run id from colliding.
 */
function freshIdempotencyKey() {
    const scenario = exec.scenario.name;
    const vu = exec.vu.idInTest;
    const iteration = exec.scenario.iterationInTest;
    const suffix = Math.random().toString(36).slice(2, 8);

    return `loadtest-${runId}-${scenario}-${vu}-${iteration}-${suffix}`;
}
