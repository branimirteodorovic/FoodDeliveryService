// Track — the post-purchase journey.
//
//   GET /orders/{id}  +  GET /delivery/orders/{id}/delivery,  every 3–5 s, until the order is done
//
// The journey with the worst read amplification per order — one placement produces tens of polls —
// and the one that keeps Redis busiest: `GetDeliveryByOrderQueryHandler` reads the row from Postgres
// and then, while the delivery is `Assigned` or `PickedUp`, the driver's live position from the GEO
// store on every single poll.
//
// **The `404` is a feature, not a tolerance.** A delivery row only exists once the restaurant marks
// the order `ReadyForPickup` — `OrderReadyForPickupIntegrationEventHandler` creates it — so between
// placement and that moment the tracking endpoint correctly answers `404 Deliveries.NotFoundForOrder`.
// The poll declares both statuses (`lib/http.js` then excludes them from `http_req_failed`) and
// records `track_delivery_visible` instead, which is the number that tells you whether the kitchen
// and driver sides are actually running. A run where that rate stays near zero is not tracking
// anything; it is polling orders nobody is cooking.
//
// Note what this journey is *not*: SignalR. `hubs/**` is out of scope for the feature (§12) — the
// negotiate-then-connect handshake is a milestone of its own and RealTime is pinned to one replica
// anyway. Polling the REST endpoint is a real client behaviour, and it is the one that loads the
// database and Redis.

import { sleep } from 'k6';
import { runId } from '../config/environments.js';
import { OUTPUT_OPTIONS, summaryFor } from '../config/output.js';
import { sloThresholds } from '../config/thresholds.js';
import { customerForThisVu, tokenFor } from '../lib/actors.js';
import {
    DELIVERY_STATUS,
    ORDER_STATUS,
    TERMINAL_DELIVERY_STATUSES,
    TERMINAL_ORDER_STATUSES,
    isStatus,
    pickRandom,
    thinkTime,
} from '../lib/domain.js';
import { announce, randomRestaurant, requireFixture, restaurantById } from '../lib/fixtures.js';
import { gatewayUrl, get } from '../lib/http.js';
import { trackDeliveryVisible, trackPolls, trackReachedTerminal } from '../lib/metrics.js';
import { placeOrder } from './order.js';

/**
 * Polls per tracking session. Bounded so an iteration ends: an order whose kitchen never runs would
 * otherwise be polled until the test does.
 *
 * The default covers roughly 45–75 s of tracking, which is the window in which an order that is
 * being driven end to end reaches at least `ReadyForPickup` — the restaurant scenario advances one
 * state per poll cycle and the delivery offer window is 30 s.
 */
const MAX_POLLS = Number(__ENV.TRACK_MAX_POLLS || 15);

const POLL_MIN_SECONDS = Number(__ENV.TRACK_POLL_MIN || 3);
const POLL_MAX_SECONDS = Number(__ENV.TRACK_POLL_MAX || 5);

export const options = {
    vus: Number(__ENV.VUS || 5),
    duration: __ENV.DURATION || '2m',
    // No threshold on `track_delivery_visible`, deliberately: it is a *diagnostic*, not an SLO. Its
    // value depends entirely on whether the kitchen and driver scenarios are running alongside —
    // standalone, almost every poll is a correct 404 — so any gate on it would fail this script for
    // doing exactly what it is supposed to. Read it in the summary; it is the first number to look
    // at when a mixed run's lifecycle looks stalled.
    thresholds: sloThresholds(),
    tags: { testid: `track-${runId}` },
    ...OUTPUT_OPTIONS,
};

/** Writes `results/…summary.{json,md}` and the terminal report. See `config/output.js`. */
export const handleSummary = summaryFor('scenarios/track.js');

export function setup() {
    requireFixture('track.js');
    announce('track.js', `${MAX_POLLS} polls max, every ${POLL_MIN_SECONDS}–${POLL_MAX_SECONDS}s`);

    return { startedOnUtc: new Date().toISOString() };
}

export default function () {
    trackJourney();
}

/**
 * Track one of this customer's orders to completion or to the poll budget.
 *
 * @param {string} [orderId] track this order — `mixed.js` does not use it, but a caller that has
 *   just placed an order can hand it straight over rather than re-reading the list.
 * @param {string} [token]
 */
export function trackJourney(orderId, token) {
    const bearer = token || tokenFor(customerForThisVu());
    const target = orderId || findSomethingToTrack(bearer);

    if (!target) {
        return;
    }

    for (let poll = 0; poll < MAX_POLLS; poll += 1) {
        trackPolls.add(1);

        const order = get(gatewayUrl(`orders/${target}`), {
            name: 'GET /orders/:id',
            token: bearer,
            body: {
                'has the requested id': (json) => Boolean(json) && json.id === target,
                'carries a status': (json) => Boolean(json) && json.status !== undefined,
            },
        });

        // 404 until the restaurant marks the order ready and the Delivery service creates the row.
        const delivery = get(gatewayUrl(`delivery/orders/${target}/delivery`), {
            name: 'GET /delivery/orders/:orderId/delivery',
            token: bearer,
            status: [200, 404],
            body: {
                'is this order\'s delivery': (json) => Boolean(json) && json.orderId === target,
            },
        });

        trackDeliveryVisible.add(delivery.response.status === 200);

        if (isTerminal(order.json, delivery.json, delivery.response.status)) {
            trackReachedTerminal.add(true);

            return;
        }

        sleep(thinkTime(POLL_MIN_SECONDS, POLL_MAX_SECONDS));
    }

    // Budget exhausted with the order still in flight. Common and not a failure — it means the
    // customer closed the app before the food arrived — but a rate near zero across a whole run
    // says the lifecycle is not completing.
    trackReachedTerminal.add(false);
}

/**
 * The customer's most recent order worth watching, placing one if they have none.
 *
 * Standalone, this script has no upstream to take an order id from, and a tracking journey with
 * nothing to track measures nothing. In a mixed run it mostly finds one immediately, because the
 * order scenario has been placing them under the same fixture customers.
 */
function findSomethingToTrack(token) {
    const orders = get(gatewayUrl('orders?page=1&pageSize=10'), {
        name: 'GET /orders',
        token,
        body: { 'body is an array': (json) => Array.isArray(json) },
    });

    // `Array.isArray`, not `|| []`. A failing request answers with a ProblemDetails *object*, which is
    // truthy — so `||` does not catch it and `.filter` throws `Object has no member 'filter'`. That is
    // not hypothetical: it is what this line did at the ramp's knee, aborting the iteration of the one
    // journey whose numbers were most interesting at exactly the moment they mattered. A saturated
    // platform is precisely when every response shape assumption in a script gets tested.
    const list = Array.isArray(orders.json) ? orders.json : [];

    const inFlight = list.filter(
        (order) => !isStatus(order.status, ORDER_STATUS, ...TERMINAL_ORDER_STATUSES)
    );

    if (inFlight.length > 0) {
        return pickRandom(inFlight).id;
    }

    // Nothing in flight. Place one — and pick the restaurant from the fixture rather than browsing,
    // because this journey's cost is the polling, and prefixing it with a full browse would smear
    // browse latency into the tracking numbers.
    const previous = pickRandom(list);
    const restaurant = (previous && restaurantById(previous.restaurantId)) || randomRestaurant();

    if (!restaurant || restaurant.menuItemIds.length === 0) {
        return null;
    }

    const placement = placeOrder(restaurant, token);

    return placement ? placement.orderId : null;
}

function isTerminal(order, delivery, deliveryStatus) {
    if (order && isStatus(order.status, ORDER_STATUS, ...TERMINAL_ORDER_STATUSES)) {
        return true;
    }

    // The delivery can reach `Delivered` a beat before the order does — Delivery publishes
    // `OrderDelivered` and the Orders service closes its own loop through the outbox, which ticks
    // every 5 s. Treating the delivery's terminal state as the end avoids polling through that lag.
    return (
        deliveryStatus === 200 &&
        Boolean(delivery) &&
        isStatus(delivery.status, DELIVERY_STATUS, ...TERMINAL_DELIVERY_STATUSES)
    );
}
