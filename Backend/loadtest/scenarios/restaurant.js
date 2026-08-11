// Restaurant — the kitchen, driving `Pending → Accepted → Preparing → ReadyForPickup`.
//
// Not a customer journey, and it is here for one reason: **without it the platform stops at
// `Pending`.** Nothing accepts an order on its own. No acceptance means no `ReadyForPickup`, which
// means the Delivery service never creates a delivery, which means no offer, no assignment, no
// pickup and no `Delivered`. The whole back half of `orders_state_transition_total` stays empty and
// the run tests roughly a third of the platform while reporting excellent numbers for it.
//
// ── The choice the plan asks to be stated ────────────────────────────────────────────────────────
//
// §4 offers two ways to drive this — a k6 scenario acting as the manager, or a background loop in
// the seeder — and asks for one to be picked and named. **This is a k6 scenario acting as the
// manager**, because:
//
//   * it is real, authenticated traffic through the Gateway, so its cost lands in the measurement
//     rather than beside it — a restaurant dashboard polling `GET /orders` is genuine production
//     load and one of the few queries that joins the manager's restaurant replica;
//   * it stops when the run stops, whereas a seeder loop is a second process with its own lifetime
//     that outlives a failed run and quietly keeps writing;
//   * the manager identity exercises `OrderOwnership.EnsureCanManage`, which an admin-driven kitchen
//     would skip entirely.
//
// The cost of that choice is a coupling worth knowing: **a manager only sees their own restaurant's
// orders**, so orders placed at restaurants outside this scenario's VU pool never progress. `setup()`
// says so out loud when the pool does not cover the catalogue, and `mixed.js` sizes the pool to
// cover it.
//
// One transition per order per poll — not all three at once. A kitchen that accepts, cooks and
// plates in the same millisecond produces a lifecycle with no queue in it, and the queue is the
// interesting part: the poll interval becomes the prep time, orders pipeline through the states, and
// `kitchen_pending_backlog` shows the kitchen falling behind before anything else does.

import { sleep } from 'k6';
import { runId } from '../config/environments.js';
import { OUTPUT_OPTIONS, summaryFor } from '../config/output.js';
import { sloThresholds } from '../config/thresholds.js';
import { managerForThisVu, tokenFor } from '../lib/actors.js';
import { ORDER_STATUS, isStatus, thinkTime } from '../lib/domain.js';
import { announce, fixture, requireFixture } from '../lib/fixtures.js';
import { gatewayUrl, get, send } from '../lib/http.js';
import { kitchenPendingBacklog, kitchenTransitionSuccess, kitchenTransitions } from '../lib/metrics.js';

/** Orders the dashboard pulls per poll. `GetOrdersQuery` has no status filter, so this is the window. */
const PAGE_SIZE = Number(__ENV.KITCHEN_PAGE_SIZE || 20);

/**
 * Transitions attempted per poll. Bounds an iteration's duration — without it, one manager facing a
 * 200-order backlog would spend a minute inside a single iteration and stop polling.
 */
const MAX_TRANSITIONS = Number(__ENV.KITCHEN_MAX_TRANSITIONS || 10);

const POLL_MIN_SECONDS = Number(__ENV.KITCHEN_POLL_MIN || 3);
const POLL_MAX_SECONDS = Number(__ENV.KITCHEN_POLL_MAX || 5);

/** The next state, and the endpoint that gets there. `Rejected` is not driven — nobody load-tests refusal. */
const NEXT_STEP = {
    Pending: { path: 'accept', to: 'Accepted' },
    Accepted: { path: 'preparing', to: 'Preparing' },
    Preparing: { path: 'ready', to: 'ReadyForPickup' },
};

export const options = {
    vus: Number(__ENV.VUS || 5),
    duration: __ENV.DURATION || '2m',
    thresholds: {
        ...sloThresholds(),
        kitchen_transition_success: ['rate>0.95'],
    },
    tags: { testid: `restaurant-${runId}` },
    ...OUTPUT_OPTIONS,
};

/** Writes `results/…summary.{json,md}` and the terminal report. See `config/output.js`. */
export const handleSummary = summaryFor('scenarios/restaurant.js');

export function setup() {
    requireFixture('restaurant.js');

    const pool = Math.min(options.vus, fixture.restaurants.length);

    announce('restaurant.js', `${pool}/${fixture.restaurants.length} restaurants covered`);

    if (pool < fixture.restaurants.length) {
        // The half-driven lifecycle the plan warns about, named at the moment it is created rather
        // than inferred from a flat state-transition panel afterwards.
        console.warn(
            `restaurant.js: ${options.vus} manager VUs cover ${pool} of ` +
                `${fixture.restaurants.length} seeded restaurants. Orders placed at the other ` +
                `${fixture.restaurants.length - pool} will sit in Pending for the whole run and ` +
                'never reach delivery — do not read the state-transition panel as complete. ' +
                `Run with --vus ${fixture.restaurants.length} to cover the catalogue.`
        );
    }

    return { startedOnUtc: new Date().toISOString() };
}

export default function () {
    kitchenJourney();
}

/**
 * One dashboard poll, one step forward for each order on it, and then the wait until the next poll.
 *
 * **The wait is inside the journey, not in the caller**, and that is not a style choice. This
 * scenario runs under `constant-vus` — an open loop with no arrival rate to pace it — so a VU that
 * returns immediately simply starts again. Measured, with the sleep left in the standalone `default`
 * and omitted from the composed path: 20 kitchen VUs and 8 driver VUs alone produced **221 requests
 * per second**, forty times the customer traffic they exist to support, and a mixed run in which
 * every percentile described the polling loop rather than the journeys. The poll interval *is* part
 * of what this journey is; it belongs with it.
 */
export function kitchenJourney() {
    // No token parameter, unlike the customer journeys: a manager's authority is scoped to their own
    // restaurant, so borrowing a caller's token would silently drive somebody else's kitchen.
    const token = tokenFor(managerForThisVu());
    const dashboard = readDashboard(token);

    if (dashboard) {
        kitchenPendingBacklog.add(
            dashboard.filter((order) => isStatus(order.status, ORDER_STATUS, 'Pending')).length
        );

        let advanced = 0;

        for (const order of dashboard) {
            if (advanced >= MAX_TRANSITIONS) {
                break;
            }

            const step = nextStepFor(order);

            if (!step) {
                continue;
            }

            advance(order.id, step, token);
            advanced += 1;
        }
    }

    sleep(thinkTime(POLL_MIN_SECONDS, POLL_MAX_SECONDS));
}

function readDashboard(token) {
    // The manager's incoming orders. Scoped in the handler by the Orders-side restaurant replica's
    // `manager_user_id` — the query is a `LEFT JOIN`, and it is one of the reads worth watching in
    // the per-request panel when the kitchen pool is large.
    const response = get(gatewayUrl(`orders?page=1&pageSize=${PAGE_SIZE}`), {
        name: 'GET /orders (kitchen)',
        token,
        body: { 'body is an array': (json) => Array.isArray(json) },
    });

    return Array.isArray(response.json) ? response.json : null;
}

function nextStepFor(order) {
    const state = Object.keys(NEXT_STEP).find((name) => isStatus(order.status, ORDER_STATUS, name));

    return state ? NEXT_STEP[state] : null;
}

function advance(orderId, step, token) {
    // `send` rather than `post`: these endpoints bind no body, and posting a JSON `null` at them
    // only invites a future model-binding change to start rejecting it.
    const result = send('POST', gatewayUrl(`orders/${orderId}/${step.path}`), null, {
        // One series per transition, not per order.
        name: `POST /orders/:id/${step.path}`,
        token,
        status: 204,
    });

    kitchenTransitions.add(1, { to: step.to });
    kitchenTransitionSuccess.add(result.ok, { to: step.to });
}
