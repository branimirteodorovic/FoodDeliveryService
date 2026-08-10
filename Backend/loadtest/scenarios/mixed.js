// Mixed — all five journeys at once, which is the only shape that measures the platform rather than
// one endpoint of it.
//
// ── Arrival rate, not virtual users ──────────────────────────────────────────────────────────────
//
// The customer journeys run under **`constant-arrival-rate`**, and that is the single most important
// decision in this file. A `constant-vus` closed loop issues its next request only after the previous
// one returns, so as the system slows the load *falls with it*: throughput plateaus, latency looks
// merely elevated, and the run reports a system coping. That is exactly backwards. Real customers do
// not slow down because the site is slow — they keep arriving, the queue grows, and the failure is a
// cliff rather than a gentle curve. Arrival rate models that, which is why Milestone D's ramp can
// find a knee at all.
//
// The two operator scenarios are `constant-vus`, and correctly so: a kitchen has a fixed number of
// staff and a city has a fixed number of drivers. They are supply, not demand — sized to the order
// rate rather than driven by it.
//
// ── Sizing ───────────────────────────────────────────────────────────────────────────────────────
//
// `RATE` is total customer arrivals per second, split browse 70% / order 20% / track 8% (§4). The
// remaining supply side is a fixed pool: one manager VU per seeded restaurant, so no order is placed
// at a restaurant nobody is cooking for, and a small driver pool (see `driver.js` for why small).
//
// Defaults are sized for a compose stack with the generator co-located — deliberately low. This is
// the gate a 5-minute run has to pass with every threshold green, not a capacity measurement.
// Milestone D's profiles are what push it.

import { runId } from '../config/environments.js';
import { sloThresholds } from '../config/thresholds.js';
import { announce, fixture, requireFixture } from '../lib/fixtures.js';
import { browseJourney } from './browse.js';
import { DRIVER_POOL, driverJourney, prepareRoster } from './driver.js';
import { kitchenJourney } from './restaurant.js';
import { orderJourney } from './order.js';
import { trackJourney } from './track.js';

/** Total customer arrivals per second across browse + order + track. */
const RATE = Number(__ENV.RATE || 2);

const DURATION = __ENV.DURATION || '5m';

/** The journey mix from §4. Shares, not counts — `arrivals()` turns them into k6 stanzas. */
const MIX = { browse: 0.7, order: 0.2, track: 0.08 };

/**
 * One manager per seeded restaurant by default. A smaller pool leaves the restaurants it does not
 * cover stuck in `Pending` for the whole run — `restaurant.js` explains why and warns when it
 * happens — and the point of the mixed run is a lifecycle that completes.
 */
const KITCHEN_VUS = Number(__ENV.KITCHEN_VUS || fixture.restaurants.length || 1);

/**
 * Small on purpose: the offer-claim workaround wastes claims linearly in the pool size. Owned by
 * `driver.js` so one number decides the VU count *and* which seeded drivers `setup()` clocks on —
 * the roster has to be knowable before any VU exists.
 */
const DRIVER_VUS = DRIVER_POOL;

export const options = {
    scenarios: {
        browse: arrivals('browse', MIX.browse),
        order: arrivals('order', MIX.order),
        track: arrivals('track', MIX.track),

        kitchen: {
            executor: 'constant-vus',
            vus: KITCHEN_VUS,
            duration: DURATION,
            exec: 'kitchen',
        },

        driver: {
            executor: 'constant-vus',
            vus: DRIVER_VUS,
            duration: DURATION,
            exec: 'driver',
        },
    },

    thresholds: {
        // The login budget is raised for this script alone, and the arithmetic is worth writing down
        // rather than hiding behind a bigger number. A mixed run starts ~58 VUs, and each one's
        // *first* iteration acquires a token — so Identity is handed roughly 60 PBKDF2 verifications
        // inside the first second or two, on a host it is already sharing with eight services and
        // the generator. Measured: p95 2.87 s, median 1.09 s, against 643 ms for smoke.js's five
        // concurrent logins and ~150 ms for one. It is a **startup transient that scales with the VU
        // count**, not the steady-state cost of signing in, and no real system has sixty users
        // arriving in the same 200 ms.
        //
        // It is raised rather than excluded because token issuance being the most expensive endpoint
        // in the system is a genuine capacity fact (Milestone F #6), and a run should not be able to
        // hide it. Milestone D replaces this guess with per-profile numbers measured from a ramp
        // whose VUs arrive gradually — which is the real fix.
        ...sloThresholds({ authP95: 4000 }),
        order_placement_duration: ['p(95)<1000'],
        order_placement_failures: ['rate<0.01'],
        order_idempotency_replay_correct: ['rate>0.99'],
        kitchen_transition_success: ['rate>0.95'],
        // The end-to-end gate. If drivers never win a claim, nothing reaches `Delivered` and the run
        // has measured browsing with extra steps.
        driver_claim_hit_rate: ['rate>0'],
    },

    tags: { testid: `mixed-${runId}` },

    // The driver roster logs in as every seeded driver once; k6's 60 s default is not enough.
    setupTimeout: __ENV.SETUP_TIMEOUT || '5m',
};

export function setup() {
    requireFixture('mixed.js');

    announce(
        'mixed.js',
        `${RATE}/s customers (browse ${pct(MIX.browse)} · order ${pct(MIX.order)} · ` +
            `track ${pct(MIX.track)}) · ${KITCHEN_VUS} kitchens · ${DRIVER_VUS} drivers · ${DURATION}`
    );

    // Clock the driven drivers on and every other seeded driver off, so the assignment candidate
    // pool contains exactly the drivers this run is driving. `driver.js` explains why that is not
    // cosmetic — without it most deliveries are parked `Unassigned` before anyone is offered them.
    prepareRoster();

    if (KITCHEN_VUS > fixture.restaurants.length) {
        console.warn(
            `mixed.js: KITCHEN_VUS=${KITCHEN_VUS} exceeds the ${fixture.restaurants.length} seeded ` +
                'restaurants; the surplus VUs duplicate managers and poll the same dashboard twice.'
        );
    }

    return { startedOnUtc: new Date().toISOString() };
}

// k6 dispatches each scenario to the named export in its `exec`. The journeys themselves live in the
// scenario files, which stay independently runnable — this file only decides how much of each.
export function browse() {
    browseJourney();
}

export function order() {
    orderJourney();
}

export function track() {
    trackJourney();
}

export function kitchen() {
    kitchenJourney();
}

export function driver() {
    driverJourney();
}

/**
 * A `constant-arrival-rate` stanza for one share of the mix.
 *
 * Expressed per **10 seconds** rather than per second because `rate` is an integer: a 70% share of
 * 2/s is 1.4 arrivals per second, which rounds to 1 (a 29% error) per second but to 14 per 10 s
 * exactly. The `timeUnit` is what keeps a stated mix the mix that actually runs.
 */
function arrivals(name, share) {
    const perTenSeconds = Math.max(1, Math.round(RATE * share * 10));

    // Each iteration holds a VU for its whole duration, think time included — browse sleeps 1–3 s
    // three times, order adds a placement, track polls for up to a minute. Under-allocating makes k6
    // drop iterations and report `dropped_iterations` instead of load, so this is generous, and
    // `maxVUs` gives it room when the platform slows and iterations stretch.
    const secondsPerIteration = name === 'track' ? 60 : 10;
    const preAllocatedVUs = Math.max(2, Math.ceil((perTenSeconds / 10) * secondsPerIteration));

    return {
        executor: 'constant-arrival-rate',
        rate: perTenSeconds,
        timeUnit: '10s',
        duration: DURATION,
        preAllocatedVUs,
        maxVUs: preAllocatedVUs * 3,
        exec: name,
    };
}

function pct(share) {
    return `${Math.round(share * 100)}%`;
}
